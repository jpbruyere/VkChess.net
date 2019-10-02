using System;
using Glfw;
using Vulkan;
using System.Threading;

namespace Crow {
	public class CrowWin : vke.VkWindow, IValueChange {
		#region IValueChange implementation
		public event EventHandler<ValueChangeEventArgs> ValueChanged;
		public virtual void NotifyValueChanged (string MemberName, object _value) {
			if (ValueChanged != null)
				ValueChanged.Invoke (this, new ValueChangeEventArgs (MemberName, _value));
		}
		#endregion

		vke.DescriptorPool descriptorPool;
		vke.DescriptorSetLayout descLayout;
		vke.DescriptorSet dsCrow;

		vke.GraphicPipeline uiPipeline;
		vke.FrameBuffer[] uiFrameBuffers;

		protected Interface crow;
		protected vkvg.Device vkvgDev;
		protected vke.Image uiImage;
		protected bool isRunning;
		protected bool rebuildBuffers;
		//protected vke.DebugDrawPipeline plDebugDraw;

		protected CrowWin (string name = "CrowWin", uint _width = 1024, uint _height = 768, bool vSync = false) :
			base (name, _width, _height, vSync) {


			Thread crowThread = new Thread (crow_thread_func);
			crowThread.IsBackground = true;
			crowThread.Start ();

			while (crow == null)
				Thread.Sleep (5);

            initUISurface ();

			initUIPipeline ();

			//plDebugDraw = new vke.DebugDrawPipeline (uiPipeline.RenderPass);
			//plDebugDraw.AddLine (Vector3.Zero, Vector3.UnitX, 1, 0, 0);
			//plDebugDraw.AddLine (Vector3.Zero, Vector3.UnitY, 0, 1, 0);
			//plDebugDraw.AddLine (Vector3.Zero, Vector3.UnitZ, 0, 0, 1);

			//plDebugDraw.AddLine (Vector3.Zero, Vector3.Zero, 1, 0, 1);
			//plDebugDraw.AddLine (Vector3.Zero, Vector3.Zero, 1, 1, 1);
			//plDebugDraw.AddLine (Vector3.Zero, Vector3.Zero, 1, 1, 0);
			//plDebugDraw.AddLine (Vector3.Zero, Vector3.Zero, 0, 1, 1);
		}

		protected override void render () {
			int idx = swapChain.GetNextImage ();

			if (idx < 0) {
				OnResize ();
				return;
			}

			lock (crow.RenderMutex) {
				presentQueue.Submit (cmds[idx], swapChain.presentComplete, drawComplete[idx]);
				presentQueue.Present (swapChain, drawComplete[idx]);
				presentQueue.WaitIdle ();
			}
			Thread.Sleep (1);
		}
        public override void Run()
        {
            onLoad();
            base.Run();
        }
        void initUIPipeline (VkSampleCountFlags samples = VkSampleCountFlags.SampleCount1) {
			descriptorPool = new vke.DescriptorPool (dev, 1, new VkDescriptorPoolSize (VkDescriptorType.CombinedImageSampler));
			descLayout = new vke.DescriptorSetLayout (dev,
				new VkDescriptorSetLayoutBinding (0, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler)
			);

			vke.GraphicPipelineConfig cfg = vke.GraphicPipelineConfig.CreateDefault (VkPrimitiveTopology.TriangleList, samples, false);
			cfg.Layout = new vke.PipelineLayout (dev, descLayout);
			cfg.RenderPass = new vke.RenderPass (dev, swapChain.ColorFormat, samples, VkAttachmentLoadOp.Load);

			cfg.AddShader (VkShaderStageFlags.Vertex, "shaders/FullScreenQuad.vert.spv");
			cfg.AddShader (VkShaderStageFlags.Fragment, "shaders/simpletexture.frag.spv");

			cfg.blendAttachments[0] = new VkPipelineColorBlendAttachmentState (true);

			uiPipeline = new vke.GraphicPipeline (cfg);

			dsCrow = descriptorPool.Allocate (descLayout);
		}
		void initUISurface () {
			lock (crow.UpdateMutex) {
				uiImage?.Dispose ();
				uiImage = new vke.Image (dev, new VkImage ((ulong)crow.surf.VkImage.ToInt64 ()), VkFormat.B8g8r8a8Unorm,
					VkImageUsageFlags.Sampled, swapChain.Width, swapChain.Height);
				uiImage.SetName ("uiImage");
				uiImage.CreateView (VkImageViewType.ImageView2D, VkImageAspectFlags.Color);
				uiImage.CreateSampler (VkFilter.Nearest, VkFilter.Nearest, VkSamplerMipmapMode.Nearest, VkSamplerAddressMode.ClampToBorder);
				uiImage.Descriptor.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
			}
		}

		void crow_thread_func () {
			vkvgDev = new vkvg.Device (instance.Handle, phy.Handle, dev.VkDev.Handle, presentQueue.qFamIndex,
	   			vkvg.SampleCount.Sample_4, presentQueue.index);

			crow = new Interface (vkvgDev, (int)swapChain.Width, (int)swapChain.Height);

			isRunning = true;	
			while (isRunning) {
				crow.Update ();
				Thread.Sleep (2);
			}

			dev.WaitIdle ();
			crow.Dispose ();
			vkvgDev.Dispose ();
			crow = null;
		}

        protected virtual void onLoad() { }
		protected void loadWindow (string path, object dataSource = null) {
			try {
				Widget w = crow.FindByName (path);
				if (w != null) {
					crow.PutOnTop (w);
					return;
				}
				w = crow.Load (path);
				w.Name = path;
				w.DataSource = dataSource;

			} catch (Exception ex) {
				System.Diagnostics.Debug.WriteLine (ex.ToString ());
			}
		}
		protected void closeWindow (string path) {
			Widget g = crow.FindByName (path);
			if (g != null)
				crow.DeleteWidget (g);
		}
		protected virtual void recordDraw (vke.CommandBuffer cmd, int imageIndex) { }

		void buildCommandBuffers () {
			for (int i = 0; i < swapChain.ImageCount; ++i) {
				cmds[i]?.Free ();
				cmds[i] = cmdPool.AllocateAndStart ();

				vke.CommandBuffer cmd = cmds[i];

				recordDraw (cmd, i);

				uiImage.SetLayout (cmd, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal,
					VkPipelineStageFlags.ColorAttachmentOutput, VkPipelineStageFlags.FragmentShader);

				uiPipeline.RenderPass.Begin (cmd, uiFrameBuffers[i]);

				uiPipeline.Bind (cmd);
				cmd.BindDescriptorSet (uiPipeline.Layout, dsCrow);


				cmd.Draw (3, 1, 0, 0);


				//plDebugDraw.RecordDraw (cmd, uiFrameBuffers[i], vkChess.VkChess.curRenderer.matrices.projection, vkChess.VkChess.curRenderer.matrices.view);

				uiPipeline.RenderPass.End (cmd);

				uiImage.SetLayout (cmd, VkImageAspectFlags.Color, VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.ColorAttachmentOptimal,
					VkPipelineStageFlags.FragmentShader, VkPipelineStageFlags.BottomOfPipe);
					
				cmds[i].End ();
			}
		}

		/// <summary>
		/// rebuild command buffers if needed
		/// </summary>
		public override void Update () {
			if (rebuildBuffers) {
				buildCommandBuffers ();
				rebuildBuffers = false;
			}
		}

		protected override void OnResize () {
			dev.WaitIdle ();

			crow.ProcessResize (new Rectangle (0, 0, (int)swapChain.Width, (int)swapChain.Height));

			initUISurface ();

			vke.DescriptorSetWrites uboUpdate = new vke.DescriptorSetWrites (dsCrow, descLayout);
			uboUpdate.Write (dev, uiImage.Descriptor);

			if (uiFrameBuffers != null)
				for (int i = 0; i < swapChain.ImageCount; ++i)
					uiFrameBuffers[i]?.Dispose ();

			uiFrameBuffers = new vke.FrameBuffer[swapChain.ImageCount];

			for (int i = 0; i < swapChain.ImageCount; ++i) {
				uiFrameBuffers[i] = new vke.FrameBuffer (uiPipeline.RenderPass, swapChain.Width, swapChain.Height,
					(uiPipeline.Samples == VkSampleCountFlags.SampleCount1) ? new vke.Image[] {
						swapChain.images[i],
					} : new vke.Image[] {
						null,
						swapChain.images[i]
					});
				uiFrameBuffers[i].SetName ("ui FB " + i);
			}

			buildCommandBuffers ();
			dev.WaitIdle ();
		}

		#region Mouse and keyboard
		protected override void onScroll (double xOffset, double yOffset) {
			if (KeyModifiers.HasFlag (Modifier.Shift))
				crow.ProcessMouseWheelChanged ((float)xOffset);
			else
				crow.ProcessMouseWheelChanged ((float)yOffset);
		}
		protected override void onMouseMove (double xPos, double yPos) {
			if (crow.ProcessMouseMove ((int)xPos, (int)yPos))
				return;
			base.onMouseMove (xPos, yPos);
		}
		protected override void onMouseButtonDown (Glfw.MouseButton button) {
			if (crow.ProcessMouseButtonDown ((MouseButton)button))
				return;
			base.onMouseButtonDown (button);
		}
		protected override void onMouseButtonUp (Glfw.MouseButton button) {
			if (crow.ProcessMouseButtonUp ((MouseButton)button))
				return;
			base.onMouseButtonUp (button);
		}
		protected override void onKeyDown (Glfw.Key key, int scanCode, Modifier modifiers) {
			if (crow.ProcessKeyDown ((Key)key))
				return;
			base.onKeyDown (key, scanCode, modifiers);
		}
		protected override void onKeyUp (Glfw.Key key, int scanCode, Modifier modifiers) {
			if (crow.ProcessKeyUp ((Key)key))
				return;
		}
		protected override void onChar (CodePoint cp) {
			if (crow.ProcessKeyPress (cp.ToChar ()))
				return;
		}
		#endregion

		#region dispose
		protected override void Dispose (bool disposing) {
			if (disposing) {
				if (!isDisposed) {
					dev.WaitIdle ();
					isRunning = false;

					for (int i = 0; i < swapChain.ImageCount; ++i)
						uiFrameBuffers[i]?.Dispose ();

					uiPipeline.Dispose ();
					descLayout.Dispose ();
					descriptorPool.Dispose ();
					//plDebugDraw.Dispose ();

					uiImage?.Dispose ();
					while (crow != null)
						Thread.Sleep (1);
				}
			}

			base.Dispose (disposing);
		}
		#endregion
	}
}
