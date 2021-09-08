﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Crow;
using vke;
using vke.glTF;
using Glfw;
using Vulkan;
using System.Reflection;

namespace vkChess
{
	public enum GameState { Init, SceneInitialized, StockFishStarted, Play, Pad, Checked, Checkmate };
	//public enum PlayerType { Human, AI };
	public enum ChessColor { White, Black };
	public enum PieceType { Pawn, Rook, Knight, Bishop, King, Queen };

	public class VkChess : CrowWindow {
#if NETCOREAPP		
		static IntPtr resolveUnmanaged (Assembly assembly, String libraryName) {
			
			switch (libraryName)
			{
				case "glfw3":
					return  System.Runtime.InteropServices.NativeLibrary.Load("glfw", assembly, null);
				case "rsvg-2.40":
					return  System.Runtime.InteropServices.NativeLibrary.Load("rsvg-2", assembly, null);
			}
			Console.WriteLine ($"[UNRESOLVE] {assembly} {libraryName}");			
			return IntPtr.Zero;
		}

		static VkChess () {
			System.Runtime.Loader.AssemblyLoadContext.Default.ResolvingUnmanagedDll+=resolveUnmanaged;
		}
#endif
		VkChess(){}
		static void Main (string [] args) {
			Instance.VALIDATION = true;
			Instance.RENDER_DOC_CAPTURE = false;
			//SwapChain.PREFERED_FORMAT = VkFormat.B8g8r8a8Unorm;
			DeferredPbrRendererBase.MAX_MATERIAL_COUNT = 3;
			DeferredPbrRendererBase.MRT_FORMAT = VkFormat.R16g16b16a16Sfloat;
			DeferredPbrRendererBase.HDR_FORMAT = VkFormat.R16g16b16a16Sfloat;
			PbrModelTexArray.TEXTURE_DIM = 512;
			ShadowMapRenderer.SHADOWMAP_SIZE = 1024;			
			using (VkChess app = new VkChess ())
				app.Run ();
		}
		public override string [] EnabledInstanceExtensions => new string [] {
#if DEBUG
			//Ext.I.VK_EXT_debug_utils,
#endif
		};

		public override string [] EnabledDeviceExtensions => new string [] {
			Ext.D.VK_KHR_swapchain,
			Ext.D.VK_KHR_multiview
		};
		protected override void configureEnabledFeatures (VkPhysicalDeviceFeatures available_features, ref VkPhysicalDeviceFeatures enabled_features) {
			base.configureEnabledFeatures (available_features, ref enabled_features);
			enabled_features.samplerAnisotropy = available_features.samplerAnisotropy;
			enabled_features.sampleRateShading = available_features.sampleRateShading;
			enabled_features.geometryShader = available_features.geometryShader;
			//enabled_features.tessellationShader = available_features.tessellationShader;
			enabled_features.textureCompressionBC = available_features.textureCompressionBC;
#if PIPELINE_STATS
			enabled_features.pipelineStatisticsQuery = available_features.pipelineStatisticsQuery;
#endif
		}
#if PIPELINE_STATS
		PipelineStatisticsQueryPool statPool;
		TimestampQueryPool timestampQPool;
		public class StatResult
		{
			public VkQueryPipelineStatisticFlags StatName;
			public ulong Value;
		}
		public StatResult [] StatResults;
#endif
#if DEBUG
		//vke.DebugUtils.Messenger dbgmsg;
#endif
		protected override void CreateRenderPass () {
			renderPass = new RenderPass (dev, swapChain.ColorFormat, DeferredPbrRendererBase.NUM_SAMPLES);			
			/*renderPass = new RenderPass (dev, VkSampleCountFlags.SampleCount1);
			renderPass.AddAttachment (swapChain.ColorFormat, VkImageLayout.PresentSrcKHR, VkSampleCountFlags.SampleCount1,
				VkAttachmentLoadOp.Load, VkAttachmentStoreOp.DontCare, VkImageLayout.ColorAttachmentOptimal);//final outpout
			SubPass subpass0 = new SubPass ();
			subpass0.AddColorReference (0, VkImageLayout.ColorAttachmentOptimal);
			renderPass.AddSubpass (subpass0);
			renderPass.AddDependency (Vk.SubpassExternal, 0,
				VkPipelineStageFlags.BottomOfPipe, VkPipelineStageFlags.ColorAttachmentOutput,
				VkAccessFlags.MemoryRead, VkAccessFlags.ColorAttachmentWrite);
			renderPass.AddDependency (0, Vk.SubpassExternal,
				VkPipelineStageFlags.ColorAttachmentOutput, VkPipelineStageFlags.BottomOfPipe,
				VkAccessFlags.ColorAttachmentWrite, VkAccessFlags.MemoryRead);*/
		}		
		GraphicPipeline plToneMap;
		DescriptorSetLayout dslToneMap;
		protected override void CreateDescriptors () {
			dsPool = new DescriptorPool (dev, 1,
				new VkDescriptorPoolSize (VkDescriptorType.CombinedImageSampler, 4),
				new VkDescriptorPoolSize (VkDescriptorType.UniformBuffer, 1));
			descSet = dsPool.Allocate (dslToneMap);
		}
		protected override void CreatePipeline () {
			using (GraphicPipelineConfig cfg = GraphicPipelineConfig.CreateDefault (VkPrimitiveTopology.TriangleList, DeferredPbrRendererBase.NUM_SAMPLES)) {			
				if (DeferredPbrRendererBase.NUM_SAMPLES != VkSampleCountFlags.SampleCount1) {
					cfg.multisampleState.sampleShadingEnable = true;
					cfg.multisampleState.minSampleShading = 0.5f;
				}
				dslToneMap = new DescriptorSetLayout (dev, 0,
					new VkDescriptorSetLayoutBinding (0, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),
					new VkDescriptorSetLayoutBinding (1, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),
					new VkDescriptorSetLayoutBinding (2, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),
					new VkDescriptorSetLayoutBinding (3, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),
					new VkDescriptorSetLayoutBinding (4, VkShaderStageFlags.Fragment, VkDescriptorType.UniformBuffer)
				);
				cfg.Layout = new PipelineLayout (dev,
					new VkPushConstantRange (VkShaderStageFlags.Fragment, 20u), dslToneMap);

				cfg.RenderPass = renderPass;
				cfg.AddShaders (
					new ShaderInfo (dev, VkShaderStageFlags.Vertex, "#vke.FullScreenQuad.vert.spv"),
					new ShaderInfo (dev, VkShaderStageFlags.Fragment, "#vkChess.net.tone_mapping2.frag.spv")
				);

				plToneMap = new GraphicPipeline (cfg);

			}
		}
		protected override void recordUICmd (PrimaryCommandBuffer cmd, int imageIndex) {			
			renderPass.Begin(cmd, frameBuffers[imageIndex]);

			cmd.SetViewport (frameBuffers[imageIndex].Width, frameBuffers[imageIndex].Height);
			cmd.SetScissor (frameBuffers[imageIndex].Width, frameBuffers[imageIndex].Height);

			cmd.PushConstant (plToneMap.Layout, VkShaderStageFlags.Fragment, 16, new float[] { Exposure, Gamma, SSRStep, SSRThreshold }, 0);
			cmd.PushConstant (plToneMap.Layout, VkShaderStageFlags.Fragment, 4, SSRMaxStepCount, 16);
			plToneMap.BindDescriptorSet (cmd, descSet);
			plToneMap.Bind (cmd);
			cmd.Draw (3, 1, 0, 0);

			renderPass.End (cmd);
		}		
		protected override void initVulkan () {
			initLog ();

			base.initVulkan ();

#if DEBUG
			/*dbgmsg = new vke.DebugUtils.Messenger (instance, VkDebugUtilsMessageTypeFlagsEXT.PerformanceEXT | VkDebugUtilsMessageTypeFlagsEXT.ValidationEXT | VkDebugUtilsMessageTypeFlagsEXT.GeneralEXT,
				VkDebugUtilsMessageSeverityFlagsEXT.InfoEXT |
				VkDebugUtilsMessageSeverityFlagsEXT.WarningEXT |
				VkDebugUtilsMessageSeverityFlagsEXT.ErrorEXT |
				VkDebugUtilsMessageSeverityFlagsEXT.VerboseEXT);*/
#endif
#if PIPELINE_STATS
			statPool = new PipelineStatisticsQueryPool (dev,
				VkQueryPipelineStatisticFlags.InputAssemblyVertices |
				VkQueryPipelineStatisticFlags.InputAssemblyPrimitives |
				VkQueryPipelineStatisticFlags.ClippingInvocations |
				VkQueryPipelineStatisticFlags.ClippingPrimitives |
				VkQueryPipelineStatisticFlags.FragmentShaderInvocations);

			timestampQPool = new TimestampQueryPool (dev);
#endif
			//Configuration.Global.Set ("StockfishPath", "/usr/games/stockfish");

			cmds = cmdPool.AllocateCommandBuffer (swapChain.ImageCount);

			createCamera ();

			DeferredPbrRendererBase.NUM_SAMPLES =  SampleCount;

			renderer = new DeferredPbrRenderer (dev, swapChain, presentQueue, cubemapPathes [1], camera.NearPlane, camera.FarPlane);

			renderer.matrices.scaleIBLAmbient = IBLAmbient;
			renderer.lights[0].color = new Vector4 (LightStrength);
			//renderer.LoadModel (transferQ, "data/models/chess.glb");
			renderer.LoadModel (transferQ, "/mnt/devel/vkChess.net/data/models/chess.glb");
			// Matrix4x4.CreateScale(1f / Math.Max(Math.Max(renderer.modelAABB.Width, renderer.modelAABB.Height), renderer.modelAABB.Depth));

			DescriptorSetWrites dsw = new DescriptorSetWrites (descSet,
				dslToneMap.Bindings[4]);
			dsw.Write (dev, renderer.uboMatrices.Descriptor);


			UpdateFrequency = 5;

			curRenderer = renderer;

			updateCellHighlighting();

			initBoard ();
			initStockfish ();

			

			iFace.Load ("ui/chess.crow").DataSource = this;

			loadCurrentGame ();
		}
		void createCamera () {
			camera = new Camera (Utils.DegreesToRadians (CameraFOV), 1f, 0.1f, 32f);
			camera.SetPosition (CameraPosition.X, CameraPosition.Y, -CameraPosition.Z);
			camera.SetRotation (CameraRotation.X, CameraRotation.Y, CameraRotation.Z);
			camera.AspectRatio = Width / Height;
			camera.Model = Matrix4x4.CreateScale (0.5f);
		}
		Queue transferQ;
		protected override void createQueues () {
			base.createQueues ();
			transferQ = new Queue (dev, VkQueueFlags.Transfer);
		}

		string [] cubemapPathes = {
			"data/textures/papermill.ktx",			
			"data/textures/pisa_cube.ktx",
			"data/textures/gcanyon_cube.ktx",
		};

		DeferredPbrRendererBase renderer;
		
		public struct InstanceData
		{
			public Vector4 color;
			public Matrix4x4 mat;

			public InstanceData (Vector4 color, Matrix4x4 mat) {
				this.color = color;
				this.mat = mat;
			}
		}

		public HostBuffer<InstanceData> instanceBuff;
		Model.InstancedCmd [] instancedCmds;

		public static DeferredPbrRendererBase curRenderer;
		bool rebuildBuffers = false;
		public virtual DeferredPbrRendererBase.DebugView CurrentDebugView {
			get => renderer.currentDebugView;
			set {
				if (value == renderer.currentDebugView)
					return;
				lock (iFace.UpdateMutex)
					renderer.currentDebugView = value;
				rebuildBuffers = true;
				NotifyValueChanged ("CurrentDebugView", renderer.currentDebugView);
			}
		}
		public PbrModelTexArray.Material[] Materials =>
			(renderer.model as PbrModelTexArray).materials;


		void buildCommandBuffers () {
			dev.WaitIdle ();

			cmdPool.Reset (); //VkCommandPoolResetFlags.ReleaseResources);

			for (int i = 0; i < swapChain.ImageCount; ++i) {
				cmds [i].Start ();
#if PIPELINE_STATS
				statPool.Begin (cmds[i]);
				renderer.recordDraw (cmds[i], i, instanceBuff, instancedCmds?.ToArray ());
				statPool.End (cmds[i]);
#else
				renderer?.recordDraw (cmds [i], i, instanceBuff, instancedCmds?.ToArray ());

				recordUICmd (cmds[i], i);
#endif
				cmds [i].End ();
			}
		}

		public override void UpdateView () {
			dev.WaitIdle ();

			renderer.UpdateView (camera);
			updateViewRequested = false;
			if (instanceBuff == null)
				return;
			if (renderer.shadowMapRenderer.updateShadowMap)
				renderer.shadowMapRenderer.update_shadow_map (cmdPool, instanceBuff, instancedCmds.ToArray ());
		}

		public static bool updateInstanceCmds = true;
		uint fpsAccum, fpsAccumCpt;

		const int fpsAccumLimit = 20;


		public override void Update () {
			base.Update ();

			dev.WaitIdle ();
#if PIPELINE_STATS
			ulong [] results = statPool.GetResults ();
			StatResults = new StatResult [statPool.RequestedStats.Length];
			for (int i = 0; i < statPool.RequestedStats.Length; i++)
				StatResults [i] = new StatResult { StatName = statPool.RequestedStats [i], Value = results [i] };
			NotifyValueChanged ("StatResults", StatResults);
#endif
			if (updateInstanceCmds) {
				updateDrawCmdList ();
				rebuildBuffers = true;
				updateInstanceCmds = false;
			}
			if (Animation.HasAnimations)
				renderer.shadowMapRenderer.updateShadowMap = true;

			fpsAccum += fps;
			fpsAccumCpt++;
			if (fpsAccumCpt == fpsAccumLimit) {
				uint fpsMean = fpsAccum / fpsAccumLimit;
				uint curFrameTime = 1000 / fpsMean;
				if (curFrameTime > UpdateFrequency)
					animationSteps = 300 / (int)curFrameTime;
				else
					animationSteps = 60;
				fpsAccum = fpsAccumCpt = 0;
			}

			Animation.ProcessAnimations ();
			//Piece.FlushHostBuffer ();

			if (instanceBuff != null && renderer.shadowMapRenderer.updateShadowMap)
				renderer.shadowMapRenderer.update_shadow_map (cmdPool, instanceBuff, instancedCmds.ToArray ());

			if (rebuildBuffers) {
				buildCommandBuffers ();
				rebuildBuffers = false;
			}
		}

		protected override void OnResize () {
			base.OnResize ();
			renderer.Resize ();

			DescriptorSetWrites dsw = new DescriptorSetWrites (descSet,
				dslToneMap.Bindings[1], dslToneMap.Bindings[2], dslToneMap.Bindings[3]);
			dsw.Write (dev, renderer.HDROutput.Descriptor, renderer.GBuffPosDepthOutput.Descriptor, renderer.GBuffN_AO.Descriptor);

			buildCommandBuffers ();
			updateViewRequested = true;
		}


		protected override void Dispose (bool disposing) {
			saveCurrentGame ();
			Configuration.Global.Save();
			if (disposing) {
				if (!isDisposed) {
					plToneMap.Dispose();
					renderer.Dispose ();
					instanceBuff.Dispose ();
				}
			}
#if DEBUG
			//dbgmsg.Dispose ();
#endif
#if PIPELINE_STATS
			timestampQPool?.Dispose ();
			statPool?.Dispose ();
#endif
			base.Dispose (disposing);
		}

		public static Vector4 UnProject (ref Matrix4x4 projection, ref Matrix4x4 view, uint width, uint height, Vector2 mouse) {
			Vector4 vec;

			vec.X = (mouse.X / (float)width * 2.0f - 1f);
			vec.Y = (mouse.Y / (float)height * 2.0f - 1f);
			vec.Z = 0f;
			vec.W = 1f;

			Matrix4x4 m;
			Matrix4x4.Invert (view * projection, out m);

			vec = Vector4.Transform (vec, m);

			if (vec.W == 0)
				return new Vector4 (0);

			vec /= vec.W;

			return vec;
		}
		protected override void onScroll (double xOffset, double yOffset) {
			base.onScroll (xOffset, yOffset);
			if (MouseIsInInterface)
				return;
			/*if (KeyModifiers.HasFlag (Modifier.Shift)) {
				if (crow.ProcessMouseWheelChanged ((float)xOffset))
					return;
			} else if (crow.ProcessMouseWheelChanged ((float)yOffset))
				return;*/
			camera.Move (0, 0, (float)yOffset * 4f);
			CameraPosition = new Vector3 (camera.Position.X, camera.Position.Y, -camera.Position.Z);	 
		}
		protected override void onMouseMove (double xPos, double yPos) {
			base.onMouseMove (xPos, yPos);

			if (MouseIsInInterface)
				return;

			double diffX = lastMouseX - xPos;
			double diffY = lastMouseY - yPos;

			if (GetButton (MouseButton.Middle) == InputAction.Press) {
				camera.Rotate ((float)-diffY, (float)-diffX);
				CameraRotation = camera.Rotation;
				return;
			}

			if (currentState < GameState.Play)
				return;

			Vector3 vMouse = UnProject (ref renderer.matrices.projection, ref renderer.matrices.view,
				swapChain.Width, swapChain.Height, new Vector2 ((float)xPos, (float)yPos)).ToVector3 ();

			Matrix4x4 invView;
			Matrix4x4.Invert (renderer.matrices.view, out invView);
			Vector3 vEye = new Vector3 (invView.M41, invView.M42, invView.M43);
			Vector3 vMouseRay = Vector3.Normalize (vMouse - vEye);

			float t = vMouse.Y / vMouseRay.Y;
			Vector3 target = vMouse - vMouseRay * t;

			Point newPos = new Point ((int)Math.Truncate (target.X + 4), (int)Math.Truncate (4f - target.Z));
			Selection = newPos;

			Piece p;
			if (selection < 0)
				p = null;
			else
				p = board [selection.X, selection.Y];
			NotifyValueChanged ("DebugCurColor", p?.Player);
			NotifyValueChanged ("DebugCurType", p?.Type);

			NotifyValueChanged ("DebugCurCellPos", p?.BoardCell);

			if (p != null) {
				NotifyValueChanged ("DebugCurCell", getChessCell (p.BoardCell.X, p.BoardCell.Y)); 
			}else
				NotifyValueChanged ("DebugCurCell", "");
		}
		protected override void onMouseButtonDown (MouseButton button) {
			base.onMouseButtonDown (button);
			if (MouseIsInInterface || button != MouseButton.Left)
				return;

			//if (waitAnimationFinished) {
			//	base.onMouseButtonDown (button);
			//	return;
			//}

			if (currentState < GameState.Play)
				return;

			if (CurrentState == GameState.Checkmate) {
				Active = -1;
				return;
			}

			if (selection < 0) {
				base.onMouseButtonDown (button);
				return;
			}

			if (Active < 0) {
				Piece p = board [Selection.X, Selection.Y];
				if (p == null)
					return;
				if (p.Player != CurrentPlayer)
					return;
				Active = Selection;
			} else if (Selection == Active) {
				Active = -1;
				return;
			} else {
				Piece p = board [Selection.X, Selection.Y];
				if (p != null) {
					if (p.Player == CurrentPlayer) {
						Active = Selection;
						return;
					}
				}

				//move
				if (ValidPositionsForActivePce == null)
					return;
				if (ValidPositionsForActivePce.Contains (Selection)) {
					//check for promotion
					Piece mp = board [Active.X, Active.Y];
					if (mp.Type == PieceType.Pawn && Selection.Y == GetPawnPromotionY(CurrentPlayer)) {
						showPromoteDialog ();
						return;
					} else
						processMove (getChessCell (Active.X, Active.Y) + getChessCell (Selection.X, Selection.Y));

					if (enableHint)
						sendToStockfish ("stop");
					else
						switchPlayer ();
				}
			}
		}
		protected override void onKeyDown (Glfw.Key key, int scanCode, Modifier modifiers) {
			switch (key) {
			case Glfw.Key.F1:
				loadWindow (@"ui/winSSR.crow", this);
				break;
			case Glfw.Key.F2:
				/*loadWindow (@"ui/board.crow", this);
				*/
				//loadWindow (@"ui/scene.crow", this.renderer.model);
				loadWindow (@"ui/board.crow", this);
				NotifyValueChanged ("board", board);
				break;
			case Glfw.Key.F3:
				checkBoardIntegrity ();
				break;
			case Glfw.Key.F4:
				if (modifiers.HasFlag (Modifier.Shift))
					loadCurrentGame ();
				else
					saveCurrentGame ();
				break;
			case Glfw.Key.Keypad0:
				whites [0].X = 0;
				whites [0].Y = 0;
				break;
			case Glfw.Key.Keypad6:
				whites [0].X++;
				break;
			case Glfw.Key.Keypad8:
				whites [0].Y++;
				break;
			case Glfw.Key.R:
				CurrentState = GameState.Play;
				resetBoard ();
				break;
			//case Glfw.Key.Enter:
			//plDebugDraw.UpdateLine (4, Vector3.Zero, vMouse, 1, 0, 1);
			//plDebugDraw.UpdateLine (5, Vector3.Zero, vEye, 1, 1, 0);
			//plDebugDraw.UpdateLine (6, vMouse, target, 1, 1, 1);
			//break;
			case Glfw.Key.Up:
				if (modifiers.HasFlag (Modifier.Shift))
					renderer.MoveLight (-Vector4.UnitZ);
				else
					camera.Move (0, 0, 1);
				updateViewRequested = true;
				break;
			case Glfw.Key.Down:
				if (modifiers.HasFlag (Modifier.Shift))
					renderer.MoveLight (Vector4.UnitZ);
				else
					camera.Move (0, 0, -1);
				updateViewRequested = true;
				break;
			case Glfw.Key.Left:
				if (modifiers.HasFlag (Modifier.Shift))
					renderer.MoveLight (-Vector4.UnitX);
				else
					camera.Move (1, 0, 0);
				updateViewRequested = true;
				break;
			case Glfw.Key.Right:
				if (modifiers.HasFlag (Modifier.Shift))
					renderer.MoveLight (Vector4.UnitX);
				else
					camera.Move (-1, 0, 0);
				updateViewRequested = true;
				break;
			case Glfw.Key.PageUp:
				if (modifiers.HasFlag (Modifier.Shift))
					renderer.MoveLight (Vector4.UnitY);
				else
					camera.Move (0, 1, 0);
				updateViewRequested = true;
				break;
			case Glfw.Key.PageDown:
				if (modifiers.HasFlag (Modifier.Shift))
					renderer.MoveLight (-Vector4.UnitY);
				else
					camera.Move (0, -1, 0);
				updateViewRequested = true;
				break;
			default:
				base.onKeyDown (key, scanCode, modifiers);
				break;
			}
		}
		
		void onQuitClick (object sender, MouseButtonEventArgs e) {
			Close ();
		}
		void undo() {
			if (currentState < GameState.Play)
				return;
			lock (movesMutex) {
				bool hintIsEnabled = EnableHint;
				EnableHint = false;
				
				//if (currentState != GameState.Pad && currentState != GameState.Checkmate && !playerIsAi (CurrentPlayer) && playerIsAi (Opponent))//undo ai move
				if (!playerIsAi (CurrentPlayer) && playerIsAi (Opponent))
					undoLastMove ();

				undoLastMove ();

				startTurn ();
				NotifyValueChanged ("board", board);

				EnableHint = hintIsEnabled;
			}
		}
		 
		void onFindStockfishPath (object sender, MouseButtonEventArgs e) {
			string stockfishDir = string.IsNullOrEmpty(StockfishPath) ?
				Directory.GetCurrentDirectory() : System.IO.Path.GetDirectoryName(StockfishPath);
			string stockfishExe = string.IsNullOrEmpty(StockfishPath) ?
				"" : System.IO.Path.GetFileName(StockfishPath);
			
			loadIMLFragment<FileDialog> (@"
				<FileDialog Caption='Select SDK Folder' CurrentDirectory='" + stockfishDir + @"' SelectedFile='" + stockfishExe + @"'
							ShowFiles='true' ShowHidden='true'/>",this)
					.OkClicked += (sender, e) => {
						StockfishPath = (sender as FileDialog).SelectedFileFullPath;
					};			
		}

		public CommandGroup Commands => new CommandGroup (
			new CommandGroup ("Menu",
				new Command ("New Game", () => loadWindow ("ui/newGame.crow", this)),
				new Command ("Save", () => loadWindow ("ui/newGame.crow", this)),
				new Command ("Load", () => loadWindow ("ui/newGame.crow", this)),
				new Command ("Options", () => loadWindow ("ui/winOptions.crow", this)),
#if DEBUG
				new Command ("Log", () => loadWindow ("ui/winLog.crow", this)),
#endif
				new Command ("Quit", () => Close())
			),
			new Command ("Undo", () => undo()),
			new Command ("Redo", () => Close())
		);
		void terminateCurrentGame () {
			if (CurrentState < GameState.Play)
				return;
			if (!playerIsAi(CurrentPlayer) && enableHint) {
				lock (movesMutex) {
					searchStopRequested = true;
					sendToStockfish ("stop");
				}
				while (searchStopRequested)
                    System.Threading.Thread.Sleep (10);
			}
		}
		public float CameraAngleY {
			get => CameraRotation.Y;
			set => CameraRotation = new Vector3(CameraRotation.X, value, CameraRotation.Z);
		}
		void onNewWhiteGame (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/newGame.crow");
			if (CurrentState < GameState.Play)
				return;			
			Log(LogType.Custom1, "New White Game");

			terminateCurrentGame();

			CurrentState = GameState.StockFishStarted;

			WhitesAreAI = false;
			BlacksAreAI = true;

			CurrentState = GameState.Play;

			resetBoard ();
			syncStockfish ();

			if (enableHint)
				sendToStockfish ("go infinite");

			Animation.StartAnimation (new AngleAnimation (this, "CameraAngleY", 0, 0.15f));
		}
		void onNewBlackGame (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/newGame.crow");
			if (CurrentState < GameState.Play)
				return;			
			Log(LogType.Custom1, "New Black Game");

			terminateCurrentGame();

			CurrentState = GameState.StockFishStarted;

			WhitesAreAI = true;
			BlacksAreAI = false;

			CurrentState = GameState.Play;

			resetBoard ();
			syncStockfish ();

			if (AISearchTime > 0)
				sendToStockfish ("go movetime " + AISearchTime.ToString());
			else
				sendToStockfish ("go depth 1");
			
			Animation.StartAnimation (new AngleAnimation (this, "CameraAngleY", MathHelper.Pi, 0.15f));			
		}
		void onPromoteToQueenClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active.X, Active.Y) + getChessCell (Selection.X, Selection.Y) + "q");
			if (enableHint)
				sendToStockfish ("stop");
			else
				switchPlayer ();
		}
		void onPromoteToBishopClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active.X, Active.Y) + getChessCell (Selection.X, Selection.Y) + "b");
			if (enableHint)
				sendToStockfish ("stop");
			else
				switchPlayer ();
		}
		void onPromoteToRookClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active.X, Active.Y) + getChessCell (Selection.X, Selection.Y) + "r");
			if (enableHint)
				sendToStockfish ("stop");
			else
				switchPlayer ();
		}
		void onPromoteToKnightClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active.X, Active.Y) + getChessCell (Selection.X, Selection.Y) + "k");
			if (enableHint)
				sendToStockfish ("stop");
			else
				switchPlayer ();
		}
		void showPromoteDialog () {
			loadWindow ("ui/promote.crow", this);
		}

		#region crow
		public float Gamma {
			get => Configuration.Global.Get<float> ("Gamma", 1.2f);
			set {
				if (value == Gamma)
					return;
				Configuration.Global.Set ("Gamma", value);
				NotifyValueChanged ("Gamma", value);
				rebuildBuffers = true;
			}
		}
		public float Exposure {
			get => Configuration.Global.Get<float> ("Exposure", 2.0f);
			set {
				if (value == Exposure)
					return;
				Configuration.Global.Set ("Exposure", value);
				NotifyValueChanged ("Exposure", value);
				rebuildBuffers = true;
			}
		}
		public float IBLAmbient {
			get => Configuration.Global.Get<float> ("IBLAmbient", 0.5f);
			set {
				if (value == IBLAmbient)
					return;
				Configuration.Global.Set ("IBLAmbient", value);
				renderer.matrices.scaleIBLAmbient = value;
				NotifyValueChanged ("IBLAmbient", value);
				updateViewRequested = true;
			}
		}
		public float LightStrength {
			get => Configuration.Global.Get<float> ("LightStrength", 1);
			set {
				if (value == LightStrength)
					return;
				Configuration.Global.Set ("LightStrength", value);
				renderer.lights [renderer.lightNumDebug].color = new Vector4 (value);
				NotifyValueChanged ("LightStrength", value);
				renderer.uboLights.Update (renderer.lights);
			}
		}
		public VkSampleCountFlags SampleCount {
			get => Configuration.Global.Get<VkSampleCountFlags> ("SampleCount", VkSampleCountFlags.SampleCount1);
			set {
				if (value == SampleCount)
					return;
				Configuration.Global.Set ("SampleCount", value);
				DeferredPbrRendererBase.NUM_SAMPLES = value;
				NotifyValueChanged ("SampleCount", value);
			}
		}
		public float SSRStep {
			get => Configuration.Global.Get<float> ("SSRStep", 0.1f);
			set {
				if (value == SSRStep)
					return;
				Configuration.Global.Set ("SSRStep", value);
				NotifyValueChanged ("SSRStep", value);
				rebuildBuffers = true;
			}
		}
		public float SSRThreshold {
			get => Configuration.Global.Get<float> ("SSRThreshold", 0.05f);
			set {
				if (value == SSRThreshold)
					return;
				Configuration.Global.Set ("SSRThreshold", value);
				NotifyValueChanged ("SSRThreshold", value);
				rebuildBuffers = true;
			}
		}
		public int SSRMaxStepCount {
			get => Configuration.Global.Get<int> ("SSRMaxStepCount", 100);
			set {
				if (value == SSRMaxStepCount)
					return;
				Configuration.Global.Set ("SSRMaxStepCount", value);
				NotifyValueChanged ("SSRMaxStepCount", value);
				rebuildBuffers = true;
			}
		}
		public float CameraFOV {
			get => Configuration.Global.Get<float> ("CameraFOV", 35f);
			set {
				if (value == CameraFOV)
					return;
				Configuration.Global.Set ("CameraFOV", value);
				NotifyValueChanged ("CameraFOV", value);
				createCamera ();
				updateViewRequested = true;
			}
		}
		/*public float CameraY {
			get => Configuration.Global.Get<float> ("CameraY", 0);
			set {
				if (value == CameraY)
					return;
				Configuration.Global.Set ("CameraY", value);
				NotifyValueChanged ("CameraY", value);
				camera.SetPosition (0, CameraY, -CameraZ);
				updateViewRequested = true;
			}
		}				
		public float CameraZ {
			get => Configuration.Global.Get<float> ("CameraZ", 12);
			set {
				if (value == CameraZ)
					return;
				Configuration.Global.Set ("CameraZ", value);
				NotifyValueChanged ("CameraZ", value);
				camera.SetPosition (0, CameraY, -CameraZ);			
				updateViewRequested = true;
			}
		}				
		public float CameraAngle {
			get => Configuration.Global.Get<float> ("CameraAngle", 0.6f);
			set {
				if (value == CameraAngle)
					return;
				Configuration.Global.Set ("CameraAngle", value);
				NotifyValueChanged ("CameraAngle", value);
				camera.SetRotation (CameraAngle, 0, 0);
				updateViewRequested = true;
			}
		}*/				
		public Vector3 CameraRotation {
			get => ExtensionMethods.ParseVec3 (Configuration.Global.Get<string> ("CameraRotation", "<0.6,0,0>"));
			set {
				if (value == CameraRotation)
					return;
				Configuration.Global.Set ("CameraRotation", value.ToString());
				NotifyValueChanged ("CameraRotation", value);
				camera.SetRotation (CameraRotation.X, CameraRotation.Y, CameraRotation.Z);
				updateViewRequested = true;
			}
		}
		public Vector3 CameraPosition {
			get => ExtensionMethods.ParseVec3 (Configuration.Global.Get<string> ("CameraPosition", "<0,0,12>"));
			set {
				if (value == CameraPosition)
					return;
				Configuration.Global.Set ("CameraPosition", value.ToString());
				NotifyValueChanged ("CameraPosition", value);
				camera.SetPosition (CameraPosition.X, CameraPosition.Y, -CameraPosition.Z);
				updateViewRequested = true;
			}
		}				
		public float CellHighlightIntensity {
			get => Configuration.Global.Get<float> ("CellHighlightIntensity", 5.0f);
			set {
				if (value == CellHighlightIntensity)
					return;
				Configuration.Global.Set ("CellHighlightIntensity", value);
				NotifyValueChanged ("CellHighlightIntensity", value);
				updateCellHighlighting();
			}
		}
		public float CellHighlightDim {
			get => Configuration.Global.Get<float> ("CellHighlightDim", 0.7f);
			set {
				if (value == CellHighlightDim)
					return;
				Configuration.Global.Set ("CellHighlightDim", value);
				NotifyValueChanged ("CellHighlightDim", value);
				updateCellHighlighting();
			}
		}
		#endregion

		#region LOGS
		public CommandGroup LogContextMenu;
		ObservableList<LogEntry> logs = new ObservableList<LogEntry>();
		public ObservableList<LogEntry> MainLog => logs;
		void initLog () {
			LogContextMenu = new CommandGroup (new Command("Clear Log", () => ResetLog()));			
		}
		public void Log(LogType type, string message) {
			if (string.IsNullOrEmpty (message))
				return;
			lock (logs)
				logs.Add (new LogEntry(type, message));
		}
		public void ResetLog () {
			lock (logs)
				logs.Clear ();
		}
		#endregion

		#region Stockfish
		object movesMutex = new object ();
		Process stockfish;
		volatile bool waitAnimationFinished;
		//Queue<string> stockfishCmdQueue = new Queue<string> ();
		List<String> stockfishMoves = new List<string> ();

		bool enableHint;
		public bool EnableHint {
			get { return enableHint; }
			set {
				if (enableHint == value)
					return;

				enableHint = value;

				BestMove = null;

				if (!playerIsAi (CurrentPlayer)) {
					if (enableHint) {
						syncStockfish ();
						sendToStockfish ("go infinite");
					} else {
						searchStopRequested = true;
						sendToStockfish ("stop");
					}
				}

				NotifyValueChanged ("EnableHint", enableHint);
			}
		}
		public bool StockfishNotFound {
			get => stockfish == null;
		}
		public string StockfishPath {
			get => Configuration.Global.Get<string> ("StockfishPath");
			set {
				if (value == StockfishPath)
					return;
				Configuration.Global.Set ("StockfishPath", value);
				NotifyValueChanged ("StockfishPath", value);

				initStockfish ();
			}
		}
		public int AISearchTime {
			get => Configuration.Global.Get<int> ("AISearchTime");
			set {
				if (value == AISearchTime)
					return;

				Configuration.Global.Set ("AISearchTime", value);
				NotifyValueChanged ("AISearchTime", value);
			}
		}
		public int WhitesLevel {
			get => Configuration.Global.Get<int> ("WhitesLevel");
			set {
				if (value == WhitesLevel)
					return;

				Configuration.Global.Set ("WhitesLevel", value);
				NotifyValueChanged ("WhitesLevel", value);
			}
		}
		public int BlacksLevel {
			get => Configuration.Global.Get<int> ("BlacksLevel");
			set {
				if (value == BlacksLevel)
					return;

				Configuration.Global.Set ("BlacksLevel", value);
				NotifyValueChanged ("BlacksLevel", value);
			}
		}
		public bool WhitesAreAI {
			get => Configuration.Global.Get<bool> ("WhitesAreAI");
			set {
				if (value == WhitesAreAI)
					return;

				if (CurrentState >= GameState.Play)
					terminateCurrentGame ();

				Configuration.Global.Set ("WhitesAreAI", value);
				NotifyValueChanged ("WhitesAreAI", value);

				if (CurrentState >= GameState.Play)
					startTurn ();
			
			}
		}
		public bool BlacksAreAI {
			get => Configuration.Global.Get<bool> ("BlacksAreAI");
			set {
				if (value == BlacksAreAI)
					return;
			
				if (CurrentState >= GameState.Play)
					terminateCurrentGame ();

				Configuration.Global.Set ("BlacksAreAI", value);
				NotifyValueChanged ("BlacksAreAI", value);

				if (CurrentState >= GameState.Play)
					startTurn ();
			
			}
		}
		string stockfishPositionCommand
			=> StockfishMoves.Count == 0 ? "" : StockfishMoves.Aggregate ((i, j) => i + " " + j);

		public bool AutoPlayHint {
			get => Configuration.Global.Get<bool> ("AutoPlayHint");
			set {
				if (value == AutoPlayHint)
					return;
				Crow.Configuration.Global.Set ("AutoPlayHint", value);
				NotifyValueChanged ("AutoPlayHint", value);
			}
		}

		public List<String> StockfishMoves {
			get => stockfishMoves;
			set { stockfishMoves = value; }
		}
		void saveCurrentGame() {
			Configuration.Global.Set ("CurrentGame", stockfishPositionCommand);
		}
		void loadCurrentGame() {

			resetBoard (false);

			string curGame = Configuration.Global.Get<string> ("CurrentGame");
			if (string.IsNullOrEmpty (curGame))
				return;
			stockfishMoves = curGame.Split (' ').ToList();

			if (currentState < GameState.Play)
				return;
			resync3DScene ();
		}
		void resync3DScene () {
			replaySilently ();

			foreach (Piece p in whites)
				p.MoveTo (p.BoardCell, true);
			foreach (Piece p in blacks)
				p.MoveTo (p.BoardCell, true);

			syncStockfish();
		}

		void initStockfish () {
			if (currentState == GameState.Init) 
				throw new Exception ("init stockfish impossible: 3d scene must be initialized first.");
			currentState = GameState.SceneInitialized;

			if (stockfish != null) {
				//resetBoard (false);

				stockfish.OutputDataReceived -= dataReceived;
				stockfish.ErrorDataReceived -= dataReceived;
				stockfish.Exited -= P_Exited;

				stockfish.Kill ();
				stockfish = null;
			}

			if (!File.Exists (StockfishPath)) {
				NotifyValueChanged ("StockfishNotFound", true);
				return;
			}
			NotifyValueChanged ("StockfishNotFound", false);

			stockfish = new Process ();
			stockfish.StartInfo.UseShellExecute = false;
			stockfish.StartInfo.RedirectStandardOutput = true;
			stockfish.StartInfo.RedirectStandardInput = true;
			stockfish.StartInfo.RedirectStandardError = true;
			stockfish.EnableRaisingEvents = true;
			stockfish.StartInfo.FileName = StockfishPath;
			stockfish.OutputDataReceived += dataReceived;
			stockfish.ErrorDataReceived += dataReceived;
			stockfish.Exited += P_Exited;
			
			stockfish.Start ();

			stockfish.BeginOutputReadLine ();
			stockfish.PriorityClass = ProcessPriorityClass.BelowNormal;
			
		}
		void syncStockfish () {
			if (currentState < GameState.Play)
				return;
			NotifyValueChanged ("StockfishMoves", StockfishMoves);
			sendToStockfish ("setoption name Skill Level value " + (CurrentPlayer == ChessColor.White ? WhitesLevel.ToString() : BlacksLevel.ToString()));
			//sendToStockfish ($"setoption name nodestime value 10");
			sendToStockfish ($"position startpos moves {stockfishPositionCommand}");
		}
		//void askStockfishIsReady () {
		//	if (waitStockfishIsReady)
		//		return;
		//	waitStockfishIsReady = true;
		//	stockfish.WaitForInputIdle ();
		//	stockfish.StandardInput.WriteLine ("isready");
		//	AddLog ("<= isready");
		//}
		void sendToStockfish (string msg) {
			if (currentState < GameState.StockFishStarted)
				return;
#if DEBUG

			Log (LogType.Normal, $"<= {msg}");
#endif
			//stockfish.WaitForInputIdle ();
			stockfish.StandardInput.WriteLine (msg);
		}
		void P_Exited (object sender, EventArgs e) {
#if DEBUG

			Log (LogType.High, "Stockfish Terminated");
#endif
		}
		volatile bool searchStopRequested;
		void dataReceived (object sender, DataReceivedEventArgs e) {
			if (string.IsNullOrEmpty (e.Data))
				return;

			lock (movesMutex) {

				string [] tmp = e.Data.Split (' ');

				//if (tmp [0] != "readyok")
#if DEBUG
				Log (LogType.Normal, $"=> {e.Data}");
#endif

				switch (tmp [0]) {
				case "readyok":
					resync3DScene();
					startTurn ();
					return;
				case "uciok":
					CurrentState = GameState.Play;
					sendToStockfish ("isready");
					break;
				case "info":
					if (playerIsAi (CurrentPlayer) || !enableHint)
						break;
					if (string.Compare (tmp [3], "seldepth", StringComparison.Ordinal) != 0)
						return;
					if (string.Compare (tmp [18], "pv", StringComparison.Ordinal) != 0)
						return;
					BestMove = tmp [19];
					break;
				case "bestmove":
					if (searchStopRequested) {
						searchStopRequested = false;
						break;
					}
					if (tmp [1] == "(none)") {
						if (checkKingIsSafe ())
							CurrentState = GameState.Pad;
						else
							CurrentState = GameState.Checkmate;
						break;
					}

					if (playerIsAi (CurrentPlayer)) {
						processMove (tmp [1]);
						switchPlayer ();
					} else if (enableHint)
						switchPlayer ();

					break;
				case "Stockfish":
					//stockfish starting
					CurrentState = GameState.StockFishStarted;
					NotifyValueChanged("StockfishVersion", tmp[1]);
					sendToStockfish ("uci");
					break;
				}
			}
		}

		#endregion

		#region game logic
		public static int animationSteps = 50;
		Vector4 bestMovePosColor, selectionPosColor, validPosColor, activeColor, kingCheckedColor;

		void updateCellHighlighting () {
			float dimC = -CellHighlightDim;
			float highC = CellHighlightIntensity;
			bestMovePosColor = new Vector4 (dimC, highC, highC, 0);
			selectionPosColor = new Vector4 (dimC, dimC, highC, 0);
			validPosColor = new Vector4 (dimC, highC, dimC, 0);
			activeColor = new Vector4 (dimC, highC, dimC, 0);
			kingCheckedColor = new Vector4 (highC, dimC, dimC, 0);
		}

		Piece [,] board;
		Piece [] whites;
		Piece [] blacks;
		bool playerIsAi (ChessColor player) => player == ChessColor.White ? WhitesAreAI : BlacksAreAI;

		volatile GameState currentState = GameState.Init;
		Point selection = new Point (-1, -1);
		Point active = new Point (-1, -1);
		List<Point> ValidPositionsForActivePce = null;

		int cptWhiteOut = 0;
		int cptBlackOut = 0;

		string bestMove;
		string BestMove {
			get => bestMove;
			set {
				if (bestMove == value)
					return;
				if (!string.IsNullOrEmpty (bestMove)) {
					Point pStart = getChessCell (bestMove.Substring (0, 2));
					Point pEnd = getChessCell (bestMove.Substring (2, 2));
					Piece.UpdateCase (pStart.X, pStart.Y, -bestMovePosColor);
					Piece.UpdateCase (pEnd.X, pEnd.Y, -bestMovePosColor);
				}

				bestMove = value;

				if (!string.IsNullOrEmpty (bestMove)) {
					Point pStart = getChessCell (bestMove.Substring (0, 2));
					Point pEnd = getChessCell (bestMove.Substring (2, 2));
					Piece.UpdateCase (pStart.X, pStart.Y, bestMovePosColor);
					Piece.UpdateCase (pEnd.X, pEnd.Y, bestMovePosColor);
				}
			}
		}

		public GameState CurrentState {
			get => currentState;
			set {
				if (currentState == value)
					return;

				if (currentState == GameState.Checked || currentState == GameState.Checkmate) {
					Point kPos = CurrentPlayerPieces.First (p => p.Type == PieceType.King).BoardCell;
					Piece.UpdateCase (kPos.X, kPos.Y, -kingCheckedColor);
				}

				currentState = value;

				if (currentState > GameState.Play) {
					Point kPos = CurrentPlayerPieces.First (p => p.Type == PieceType.King).BoardCell;
					Piece.UpdateCase (kPos.X, kPos.Y, kingCheckedColor);
					if (currentState == GameState.Checkmate) {
						Piece king = CurrentPlayerPieces.First (p => p.Type == PieceType.King);
						Animation.StartAnimation (new FloatAnimation (king, "Z", 0.4f, 0.04f));
						Animation.StartAnimation (new AngleAnimation (king, "XAngle", MathHelper.Pi * 0.55f, 0.09f));
						Animation.StartAnimation (new AngleAnimation (king, "ZAngle", king.ZAngle + 0.3f, 0.5f));
					}
				}

				NotifyValueChanged ("CurrentState", currentState);
			}
		}
		ChessColor currentPlayer;

		public ChessColor CurrentPlayer {
			get => currentPlayer;
			set {
				if (currentPlayer == value)
					return;
				currentPlayer = value;
				NotifyValueChanged ("CurrentPlayer", currentPlayer);
			}
		}

		public ChessColor Opponent {
			get { return CurrentPlayer == ChessColor.White ? ChessColor.Black : ChessColor.White; }
		}
		public Piece [] OpponentPieces {
			get { return CurrentPlayer == ChessColor.White ? blacks : whites; }
		}
		public Piece [] CurrentPlayerPieces {
			get { return CurrentPlayer == ChessColor.White ? whites : blacks; }
		}
		public int GetPawnPromotionY (ChessColor color) => color == ChessColor.White ? 7 : 0;

		Point Active {
			get {
				return active;
			}
			set {
				if (active == value)
					return;

				if (active >= 0)
					Piece.UpdateCase (active.X, active.Y, -activeColor);

				active = value;

				if (ValidPositionsForActivePce != null) {
					foreach (Point vp in ValidPositionsForActivePce)
						Piece.UpdateCase (vp.X, vp.Y, -validPosColor);
					ValidPositionsForActivePce = null;
				}

				if (active < 0) {
					NotifyValueChanged ("ActCell", "");
					return;
				}

				NotifyValueChanged ("ActCell", getChessCell (active.X, active.Y));
				Piece.UpdateCase (active.X, active.Y, activeColor);

				ValidPositionsForActivePce = new List<Point> ();

				foreach (string s in computeValidMove (Active)) {
					bool kingIsSafe = true;

					previewBoard (s);

					kingIsSafe = checkKingIsSafe ();

					restoreBoardAfterPreview ();

					if (kingIsSafe)
						addValidMove (getChessCell (s.Substring (2, 2)));
				}

				if (ValidPositionsForActivePce.Count == 0)
					ValidPositionsForActivePce = null;

				if (ValidPositionsForActivePce != null) {
					foreach (Point vp in ValidPositionsForActivePce)
						Piece.UpdateCase (vp.X, vp.Y, validPosColor);
				}
			}
		}

		Point Selection {
			get {
				return selection;
			}
			set {
				if (selection == value)
					return;
				if (selection >= 0)
					Piece.UpdateCase (selection.X, selection.Y, -selectionPosColor);

				selection = value;

				if (selection.X < 0)
					selection = -1;
				else if (selection.X > 7)
					selection = -1;
				if (selection.Y < 0)
					selection = -1;
				else if (selection.Y > 7)
					selection = -1;

				if (selection >= 0)
					Piece.UpdateCase (selection.X, selection.Y, selectionPosColor);
				NotifyValueChanged ("SelCell", getChessCell (selection.X, selection.Y));
			}
		}
		int[] mesheIndexes;
		int [] boardCellMesheIndices;

		void initBoard () {
			currentState = GameState.Init;
			CurrentPlayer = ChessColor.White;
			cptWhiteOut = 0;
			cptBlackOut = 0;
			StockfishMoves.Clear ();
			NotifyValueChanged ("StockfishMoves", StockfishMoves);

			Active = -1;

			board = new Piece [8, 8];
			instanceBuff = new HostBuffer<InstanceData> (dev, VkBufferUsageFlags.VertexBuffer, 97, true, false);
			instanceBuff.SetName ("instances buff");

			Piece.instanceBuff = instanceBuff;

			mesheIndexes = new int [6];
			mesheIndexes [(int)PieceType.Pawn] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("pawn").Mesh);
			mesheIndexes [(int)PieceType.Rook] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("rook").Mesh);
			mesheIndexes [(int)PieceType.Knight] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("knight").Mesh);
			mesheIndexes [(int)PieceType.Bishop] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("bishop").Mesh);
			mesheIndexes [(int)PieceType.King] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("king").Mesh);
			mesheIndexes [(int)PieceType.Queen] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("queen").Mesh);

			for (int i = 0; i < 8; i++)
				addPiece (ChessColor.White, PieceType.Pawn, i, 1);
			for (int i = 0; i < 8; i++)
				addPiece (ChessColor.Black, PieceType.Pawn, i, 6);
				
			addPiece (ChessColor.White, PieceType.Bishop, 2, 0);
			addPiece (ChessColor.White, PieceType.Bishop, 5, 0);
			addPiece (ChessColor.Black, PieceType.Bishop, 2, 7);
			addPiece (ChessColor.Black, PieceType.Bishop, 5, 7);

			addPiece (ChessColor.White, PieceType.Knight, 1, 0);
			addPiece (ChessColor.White, PieceType.Knight, 6, 0);
			addPiece (ChessColor.Black, PieceType.Knight, 1, 7);
			addPiece (ChessColor.Black, PieceType.Knight, 6, 7);

			addPiece (ChessColor.White, PieceType.Rook, 0, 0);
			addPiece (ChessColor.White, PieceType.Rook, 7, 0);
			addPiece (ChessColor.Black, PieceType.Rook, 0, 7);
			addPiece (ChessColor.Black, PieceType.Rook, 7, 7);

			addPiece (ChessColor.White, PieceType.Queen, 3, 0);
			addPiece (ChessColor.Black, PieceType.Queen, 3, 7);

			addPiece (ChessColor.White, PieceType.King, 4, 0);
			addPiece (ChessColor.Black, PieceType.King, 4, 7);

			whites = board.Cast<Piece> ().Where (p => p?.Player == ChessColor.White).ToArray ();
			blacks = board.Cast<Piece> ().Where (p => p?.Player == ChessColor.Black).ToArray ();

			Piece.boardDatas = new InstanceData [64];

			uint curInstIdx = 32;
			boardCellMesheIndices = new int [65];

			for (int x = 0; x < 8; x++) {
				for (int y = 0; y < 8; y++) {
					string name = string.Format ($"{(char)(y + 97)}{(x + 1).ToString ()}");
					int boardDataIdx = x * 8 + y;
					boardCellMesheIndices [boardDataIdx] = renderer.model.Meshes.IndexOf (renderer.model.FindNode (name).Mesh);
					Piece.boardDatas [boardDataIdx] = new InstanceData (new Vector4 (1), Matrix4x4.Identity);
					instanceBuff.Update (curInstIdx, Piece.boardDatas [boardDataIdx]);
					curInstIdx++;
				}
			}
			boardCellMesheIndices [64] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("frame").Mesh);
			instanceBuff.Update (96, new InstanceData (new Vector4 (1), Matrix4x4.Identity));
			Piece.flushEnd = 97;

			updateDrawCmdList ();
			currentState = GameState.SceneInitialized;
		}

		void updateDrawCmdList () {
			List<Model.InstancedCmd> primitiveCmds = new List<Model.InstancedCmd> ();
			Piece [] pieces = whites.Concat (blacks).ToArray ();

			uint instIdx = 0;
			updateDrawCmdList (PieceType.Pawn, ref instIdx, ref pieces, ref primitiveCmds);
			updateDrawCmdList (PieceType.Rook, ref instIdx, ref pieces, ref primitiveCmds);
			updateDrawCmdList (PieceType.Bishop, ref instIdx, ref pieces, ref primitiveCmds);
			updateDrawCmdList (PieceType.Knight, ref instIdx, ref pieces, ref primitiveCmds);
			updateDrawCmdList (PieceType.Queen, ref instIdx, ref pieces, ref primitiveCmds);
			updateDrawCmdList (PieceType.King, ref instIdx, ref pieces, ref primitiveCmds);

			foreach (int i in boardCellMesheIndices) 
				primitiveCmds.Add (new Model.InstancedCmd { count = 1, meshIdx = i });

			instancedCmds = primitiveCmds.ToArray ();
		}

		void updateDrawCmdList (PieceType pieceType, ref uint instIdx, ref Piece[] pieces, ref List<Model.InstancedCmd> primitiveCmds) {
			IEnumerable<Piece> tmp = pieces.Where (p => p.Type == pieceType);
			primitiveCmds.Add (new Model.InstancedCmd { count = (uint)tmp.Count (), meshIdx = mesheIndexes [(int)pieceType] });
			foreach (Piece p in tmp) {
				p.instanceIdx = instIdx;
				p.updatePos ();
				instIdx++;
			}
		}

		void resetBoard (bool animate = true) {
			CurrentPlayer = ChessColor.White;
			BestMove = null;

			cptWhiteOut = 0;
			cptBlackOut = 0;
			StockfishMoves.Clear ();
			NotifyValueChanged ("StockfishMoves", StockfishMoves);

			Active = -1;
			board = new Piece [8, 8];

			foreach (Piece p in whites) {
				p.Reset (animate);
				board [p.initX, p.initY] = p;
			}
			foreach (Piece p in blacks) {
				p.Reset (animate);
				board [p.initX, p.initY] = p;
			}
		}
		void saveGame (string fileName = "default.chess") {
			string defaultSaveDirectory = System.IO.Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), ".config",
				System.Reflection.Assembly.GetEntryAssembly ().GetName().Name, "SavedGames");
			if (!Directory.Exists (defaultSaveDirectory))
				Directory.CreateDirectory (defaultSaveDirectory);

			string filePath = System.IO.Path.Combine (defaultSaveDirectory, fileName);
			using (Stream stream = new FileStream (filePath, FileMode.Create))
				using (StreamWriter sw = new StreamWriter (stream))
					sw.WriteLine (stockfishPositionCommand);
		}
		void loadGame (string fileName = "default.chess") {

		}
		void addPiece (ChessColor player, PieceType _type, int col, int line) {
			Piece p = new Piece (player, _type, col, line);
			board [col, line] = p;
		}

		void addValidMove (Point p) {
			if (ValidPositionsForActivePce.Contains (p))
				return;
			ValidPositionsForActivePce.Add (p);
		}

		bool checkKingIsSafe () {
			foreach (Piece op in OpponentPieces) {
				if (op.Captured)
					continue;
				foreach (string opM in computeValidMove (op.BoardCell)) {
					if (opM.EndsWith ("K"))
						return false;
				}
			}
			return true;
		}
		string [] getLegalMoves () {

			List<String> legalMoves = new List<string> ();

			foreach (Piece p in CurrentPlayerPieces) {
				if (p.Captured)
					continue;
				foreach (string s in computeValidMove (p.BoardCell)) {
					bool kingIsSafe = true;

					previewBoard (s);

					kingIsSafe = checkKingIsSafe ();

					restoreBoardAfterPreview ();

					if (kingIsSafe)
						legalMoves.Add (s);
				}
			}
			return legalMoves.ToArray ();
		}
		string [] checkSingleMove (Point pos, int xDelta, int yDelta) {
			int x = pos.X + xDelta;
			int y = pos.Y + yDelta;

			if (x < 0 || x > 7 || y < 0 || y > 7)
				return null;

			if (board [x, y] == null) {
				if (board [pos.X, pos.Y].Type == PieceType.Pawn) {
					if (xDelta != 0) {
						//check En passant capturing
						int epY;
						string validEP;
						if (board [pos.X, pos.Y].Player == ChessColor.White) {
							epY = 4;
							validEP = getChessCell (x, 6) + getChessCell (x, 4);
						} else {
							epY = 3;
							validEP = getChessCell (x, 1) + getChessCell (x, 3);
						}
						if (pos.Y != epY)
							return null;
						if (board [x, epY] == null)
							return null;
						if (board [x, epY].Type != PieceType.Pawn)
							return null;
						if (StockfishMoves [StockfishMoves.Count - 1] != validEP)
							return null;
						return new string [] { getChessCell (pos.X, pos.Y) + getChessCell (x, y) + "EP" };
					}
					//check pawn promotion
					if (y == GetPawnPromotionY (board [pos.X, pos.Y].Player)) {
						string basicPawnMove = getChessCell (pos.X, pos.Y) + getChessCell (x, y);
						return new string [] {
				basicPawnMove + "q",
				basicPawnMove + "k",
				basicPawnMove + "r",
				basicPawnMove + "b"
			};
					}
				}
				return new string [] { getChessCell (pos.X, pos.Y) + getChessCell (x, y) };
			}

			if (board [x, y].Player == board [pos.X, pos.Y].Player)
				return null;
			if (board [pos.X, pos.Y].Type == PieceType.Pawn && xDelta == 0)
				return null;//pawn cant take in front

			if (board [x, y].Type == PieceType.King)
				return new string [] { getChessCell (pos.X, pos.Y) + getChessCell (x, y) + "K" };

			if (board [pos.X, pos.Y].Type == PieceType.Pawn &&
				y == GetPawnPromotionY (board [pos.X, pos.Y].Player)) {
				string basicPawnMove = getChessCell (pos.X, pos.Y) + getChessCell (x, y);
				return new string [] {
			basicPawnMove + "q",
			basicPawnMove + "k",
			basicPawnMove + "r",
			basicPawnMove + "b"
		};
			}

			return new string [] { getChessCell (pos.X, pos.Y) + getChessCell (x, y) };
		}
		string [] checkIncrementalMove (Point pos, int xDelta, int yDelta) {

			List<string> legalMoves = new List<string> ();

			int x = pos.X + xDelta;
			int y = pos.Y + yDelta;

			string strStart = getChessCell (pos.X, pos.Y);

			while (x >= 0 && x < 8 && y >= 0 && y < 8) {
				if (board [x, y] == null) {
					legalMoves.Add (strStart + getChessCell (x, y));
					x += xDelta;
					y += yDelta;
					continue;
				}

				if (board [x, y].Player == board [pos.X, pos.Y].Player)
					break;

				if (board [x, y].Type == PieceType.King)
					legalMoves.Add (strStart + getChessCell (x, y) + "K");
				else
					legalMoves.Add (strStart + getChessCell (x, y));

				break;
			}
			return legalMoves.ToArray ();
		}
		string [] computeValidMove (Point pos) {
			int x = pos.X;
			int y = pos.Y;

			Piece p = board [x, y];

			ChessMoves validMoves = new ChessMoves ();

			if (p != null) {
				switch (p.Type) {
				case PieceType.Pawn:
					int pawnDirection = 1;
					if (p.Player == ChessColor.Black)
						pawnDirection = -1;
					validMoves.AddMove (checkSingleMove (pos, 0, 1 * pawnDirection));
					if (!p.HasMoved && board [x, y + pawnDirection] == null)
						validMoves.AddMove (checkSingleMove (pos, 0, 2 * pawnDirection));
					validMoves.AddMove (checkSingleMove (pos, -1, 1 * pawnDirection));
					validMoves.AddMove (checkSingleMove (pos, 1, 1 * pawnDirection));
					break;
				case PieceType.Rook:
					validMoves.AddMove (checkIncrementalMove (pos, 0, 1));
					validMoves.AddMove (checkIncrementalMove (pos, 0, -1));
					validMoves.AddMove (checkIncrementalMove (pos, 1, 0));
					validMoves.AddMove (checkIncrementalMove (pos, -1, 0));
					break;
				case PieceType.Knight:
					validMoves.AddMove (checkSingleMove (pos, 2, 1));
					validMoves.AddMove (checkSingleMove (pos, 2, -1));
					validMoves.AddMove (checkSingleMove (pos, -2, 1));
					validMoves.AddMove (checkSingleMove (pos, -2, -1));
					validMoves.AddMove (checkSingleMove (pos, 1, 2));
					validMoves.AddMove (checkSingleMove (pos, -1, 2));
					validMoves.AddMove (checkSingleMove (pos, 1, -2));
					validMoves.AddMove (checkSingleMove (pos, -1, -2));
					break;
				case PieceType.Bishop:
					validMoves.AddMove (checkIncrementalMove (pos, 1, 1));
					validMoves.AddMove (checkIncrementalMove (pos, -1, -1));
					validMoves.AddMove (checkIncrementalMove (pos, 1, -1));
					validMoves.AddMove (checkIncrementalMove (pos, -1, 1));
					break;
				case PieceType.King:
					if (!p.HasMoved) {
						Piece tower = board [0, y];
						if (tower != null) {
							if (!tower.HasMoved) {
								for (int i = 1; i < x; i++) {
									if (board [i, y] != null)
										break;
									if (i == x - 1)
										validMoves.Add (getChessCell (x, y) + getChessCell (x - 2, y));
								}
							}
						}
						tower = board [7, y];
						if (tower != null) {
							if (!tower.HasMoved) {
								for (int i = x + 1; i < 7; i++) {
									if (board [i, y] != null)
										break;
									if (i == 6)
										validMoves.Add (getChessCell (x, y) + getChessCell (x + 2, y));
								}
							}
						}
					}

					validMoves.AddMove (checkSingleMove (pos, -1, -1));
					validMoves.AddMove (checkSingleMove (pos, -1, 0));
					validMoves.AddMove (checkSingleMove (pos, -1, 1));
					validMoves.AddMove (checkSingleMove (pos, 0, -1));
					validMoves.AddMove (checkSingleMove (pos, 0, 1));
					validMoves.AddMove (checkSingleMove (pos, 1, -1));
					validMoves.AddMove (checkSingleMove (pos, 1, 0));
					validMoves.AddMove (checkSingleMove (pos, 1, 1));

					break;
				case PieceType.Queen:
					validMoves.AddMove (checkIncrementalMove (pos, 0, 1));
					validMoves.AddMove (checkIncrementalMove (pos, 0, -1));
					validMoves.AddMove (checkIncrementalMove (pos, 1, 0));
					validMoves.AddMove (checkIncrementalMove (pos, -1, 0));
					validMoves.AddMove (checkIncrementalMove (pos, 1, 1));
					validMoves.AddMove (checkIncrementalMove (pos, -1, -1));
					validMoves.AddMove (checkIncrementalMove (pos, 1, -1));
					validMoves.AddMove (checkIncrementalMove (pos, -1, 1));
					break;
				}
			}
			return validMoves.ToArray ();
		}

		string preview_Move;
		bool preview_MoveState;
		bool preview_wasPromoted;
		Piece preview_Captured;

		void checkBoardIntegrity () {
			bool integrity = true;
			for (int i = 0; i < 8; i++) {
				for (int j = 0; j < 8; j++) {
					Piece p = board [i, j];
					if (p == null)
						continue;
					if (p.BoardCell == new Point (i, j))
						continue;

					Console.WriteLine ($"incoherence for cell {i},{j}: piece:{p.Player} {p.Type} has cell {p.BoardCell}");
					integrity = false;
				}
			}
			foreach (Piece p in whites) {
				if (p.Captured)
					continue;
				if (board[p.BoardCell.X,p.BoardCell.Y] == p)
					continue;
				Console.WriteLine ($"incoherence for piece:{p.Player} {p.Type} has cell {p.BoardCell}");
				integrity = false;
			}
			foreach (Piece p in blacks) {
				if (p.Captured)
					continue;
				if (board [p.BoardCell.X, p.BoardCell.Y] == p)
					continue;
				Console.WriteLine ($"incoherence for piece:{p.Player} {p.Type} has cell {p.BoardCell}");
				integrity = false;
			}
			if (integrity)
				Console.WriteLine ("Board integrity ok");
		}
		void previewBoard (string move) {
			if (move.EndsWith ("K")) {
#if DEBUG
				Log (LogType.Debug, $"Previewing: {move}");
#endif
				move = move.Substring (0, 4);
			}

			preview_Move = move;

			Point pStart = getChessCell (preview_Move.Substring (0, 2));
			Point pEnd = getChessCell (preview_Move.Substring (2, 2));
			Piece p = board [pStart.X, pStart.Y];

			//pawn promotion
			if (preview_Move.Length == 5) {
				p.Promote (preview_Move [4], true);
				preview_wasPromoted = true;
			} else
				preview_wasPromoted = false;

			preview_MoveState = p.HasMoved;
			board [pStart.X, pStart.Y] = null;
			p.HasMoved = true;
			p.BoardCell = pEnd;

			//pawn en passant
			if (preview_Move.Length == 6)
				preview_Captured = board [pEnd.X, pStart.Y];
			else
				preview_Captured = board [pEnd.X, pEnd.Y];

			if (preview_Captured != null) 
				preview_Captured.Captured = true;

			board [pEnd.X, pEnd.Y] = p;
		}
		void restoreBoardAfterPreview () {
			Point pStart = getChessCell (preview_Move.Substring (0, 2));
			Point pEnd = getChessCell (preview_Move.Substring (2, 2));
			Piece p = board [pEnd.X, pEnd.Y];
			p.HasMoved = preview_MoveState;
			if (preview_wasPromoted)
				p.Unpromote ();
			board [pStart.X, pStart.Y] = p;
			p.BoardCell = pStart;
			board [pEnd.X, pEnd.Y] = null;
			if (preview_Move.Length == 6) {//en passant
				board [pEnd.X, pStart.Y] = preview_Captured;
				preview_Captured.BoardCell = new Point (pEnd.X, pStart.Y);
			} else {
				board [pEnd.X, pEnd.Y] = preview_Captured;
				if (preview_Captured!= null)
					preview_Captured.BoardCell = pEnd;
			}
			preview_Move = null;
			preview_Captured = null;
		}

		string getChessCell (int col, int line) {
			if (col < 0 || line < 0)
				return null;
			char c = (char)(col + 97);
			return c.ToString () + (line + 1).ToString ();
		}
		Point getChessCell (string s) {
			return new Point ((int)s [0] - 97, int.Parse (s [1].ToString ()) - 1);
		}
		Vector3 getCurrentCapturePosition (Piece p) {
			float x, y;
			if (p.Player == ChessColor.White) {
				x = -2.0f;
				y = 6.5f - (float)cptWhiteOut * 0.7f;
				if (cptWhiteOut > 7) {
					x -= 0.7f;
					y += 8f * 0.7f;
				}
			} else {
				x = 9.0f;
				y = 1.5f + (float)cptBlackOut * 0.7f;
				if (cptBlackOut > 7) {
					x += 0.7f;
					y -= 8f * 0.7f;
				}
			}
			return new Vector3 (x, y, -0.25f);
		}

		void capturePiece (Piece p, bool animate = true) {
			Point pos = p.BoardCell;
			board [pos.X, pos.Y] = null;

			Vector3 capturePos = getCurrentCapturePosition (p);

			if (p.Player == ChessColor.White)
				cptWhiteOut++;
			else
				cptBlackOut++;

			p.SetCaptured (capturePos);

			if (animate)
				Animation.StartAnimation (new PathAnimation (p, "Position",
					new BezierPath (
					p.Position,
					capturePos, Vector3.UnitZ), animationSteps));
		}

		void processMove (string move, bool animate = true) {

			if (string.IsNullOrEmpty (move))
				return;
			if (move == "(none)")
				return;

			CurrentState = GameState.Play;

			Point pStart = getChessCell (move.Substring (0, 2));
			Point pEnd = getChessCell (move.Substring (2, 2));

			Piece p = board [pStart.X, pStart.Y];
			if (p == null) {
#if DEBUG
				Log (LogType.Error, $"impossible move: {move}");
#endif
				return;
			}

			bool enPassant = false;
			if (p.Type == PieceType.Pawn && pStart.X != pEnd.X && board [pEnd.X, pEnd.Y] == null)
				enPassant = true;

			StockfishMoves.Add (move);
			NotifyValueChanged ("StockfishMoves", StockfishMoves);

			board [pStart.X, pStart.Y] = null;
			Point pTarget = pEnd;
			if (enPassant)
				pTarget.Y = pStart.Y;
			if (board [pTarget.X, pTarget.Y] != null)
				capturePiece (board [pTarget.X, pTarget.Y], animate);
			board [pEnd.X, pEnd.Y] = p;
			p.HasMoved = true;

			p.MoveTo (pEnd, animate);

			Active = -1;

			if (!enPassant) {
				//check if rockMove
				if (p.Type == PieceType.King) {
					int xDelta = pStart.X - pEnd.X;
					if (Math.Abs (xDelta) == 2) {
						//rocking
						if (xDelta > 0) {
							pStart.X = 0;
							pEnd.X = pEnd.X + 1;
						} else {
							pStart.X = 7;
							pEnd.X = pEnd.X - 1;
						}
						p = board [pStart.X, pStart.Y];
						board [pStart.X, pStart.Y] = null;
						board [pEnd.X, pEnd.Y] = p;
						p.HasMoved = true;
						p.MoveTo (pEnd, animate);
					}
				}

				//check promotion
				if (move.Length == 5)
					p.Promote (move [4]);
			}
			NotifyValueChanged ("board", board);
		}

		void undoLastMove () {
			if (StockfishMoves.Count == 0)
				return;

			if (currentState == GameState.Checked || currentState == GameState.Checkmate) {
				Point kPos = CurrentPlayerPieces.First (p => p.Type == PieceType.King).BoardCell;
				Piece.UpdateCase (kPos.X, kPos.Y, -kingCheckedColor);
			}

			string move = StockfishMoves [StockfishMoves.Count - 1];
			StockfishMoves.RemoveAt (StockfishMoves.Count - 1);

			Point pPreviousPos = getChessCell (move.Substring (0, 2));
			Point pCurPos = getChessCell (move.Substring (2, 2));

			Piece p = board [pCurPos.X, pCurPos.Y];

			replaySilently ();

			Piece pCaptured = board [pCurPos.X, pCurPos.Y];

			p.MoveTo (pPreviousPos, true);

			if (p.Type == PieceType.King){
				int difX = pCurPos.X - pPreviousPos.X;
				if (Math.Abs (difX) == 2) {
					//rocking
					int y = p.Player == ChessColor.White ? 0 : 7;
					Piece rook = difX > 0 ? board [7, y] : board [0, y];
					rook.MoveTo (new Point (difX > 0 ? 7 : 0, y), true);
				}
			}

			//animate undo capture
			if (pCaptured == null)
				return;
			Vector3 pCapLastPos = new Vector3 (pCaptured.BoardCell.X, pCaptured.BoardCell.Y, 0);
			//pCaptured.Position = getCurrentCapturePosition (pCaptured);

			Animation.StartAnimation (new PathAnimation (pCaptured, "Position",
				new BezierPath (
				pCaptured.Position,
				pCapLastPos, Vector3.UnitZ),animationSteps));
			NotifyValueChanged ("board", board);				
		}
		void replaySilently () {
			string [] moves = StockfishMoves.ToArray ();
			currentState = GameState.Play;
			resetBoard (false);
			foreach (string m in moves) {
				processMove (m, false);
				CurrentPlayer = Opponent;
			}
		}

		void startTurn () {
			syncStockfish ();
			NotifyValueChanged ("board", board);

			bool kingIsSafe = checkKingIsSafe ();
			if (getLegalMoves ().Length == 0) {
				if (kingIsSafe)
					CurrentState = GameState.Pad;
				else {
					CurrentState = GameState.Checkmate;
					return;
				}
			} else if (!kingIsSafe)			
				CurrentState = GameState.Checked;

			//if (currentState != GameState.Play) {
			//	WhitesAreAI = false;
			//	BlacksAreAI = false;
			//}

			if (playerIsAi (CurrentPlayer)) {
				if (AISearchTime > 0)
					sendToStockfish ("go movetime " + AISearchTime.ToString());
				else
					sendToStockfish ("go depth 1");
			}else if (enableHint)
				sendToStockfish ("go infinite");
		}
		void switchPlayer () {
			BestMove = null;
			CurrentState = GameState.Play;

			CurrentPlayer = Opponent;			
			startTurn ();
		}

		void move_AnimationFinished (Animation a) {
			waitAnimationFinished = false;
		}

		#endregion
	}
}