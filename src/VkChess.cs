using System;
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
using System.Net;
using System.Runtime.InteropServices;
using Window = Crow.Window;
using System.Text;

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
				case "cairo":
					return System.Runtime.InteropServices.NativeLibrary.Load("cairo-2", assembly, null);
				case "glfw3":
					return System.Runtime.InteropServices.NativeLibrary.Load("glfw", assembly, null);
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
			//Instance.VALIDATION = true;
			//Instance.RENDER_DOC_CAPTURE = true;
			//SwapChain.PREFERED_FORMAT = VkFormat.B8g8r8a8Unorm;
			DeferredPbrRendererBase.MAX_MATERIAL_COUNT = 3;
			PbrModelTexArray.TEXTURE_DIM = 1024;
			ShadowMapRenderer.SHADOWMAP_SIZE = 1024;
			using (VkChess app = new VkChess ())
				app.Run ();
		}

		public CommandGroup Commands, IconBar;
		public Command CMDUndo, CMDOptions, CMDViewBoard, CMDViewMoves, CMDLoadSelectedFile;
		public ToggleCommand CMDToggleHint;
		void initCommands () {
			CMDUndo = new ActionCommand ("Undo", () => undo(), "#icons.undo.svg");
			CMDOptions = new ActionCommand ("Options", () => loadWindow ("#ui.winOptions.crow", this), "#icons.options.svg");
			CMDViewMoves = new ActionCommand ("Moves", () => loadWindow ("#ui.winMoves.crow", this), "#icons.moves.svg");
			CMDViewBoard = new ActionCommand ("Mini Board", () => loadWindow ("#ui.winBoard.crow", this), "#icons.board.svg");
			CMDLoadSelectedFile = new ActionCommand ("Load", () => loadGameFromFile (selectedSavedGame), "#icons.load.svg");
			CMDToggleHint = new ToggleCommand (this, "Hint", new Binding<bool> ("EnableHint"), "#icons.hint.svg", null, true);

			IconBar = new CommandGroup (CMDOptions, CMDViewBoard, CMDViewMoves, CMDUndo, CMDToggleHint);

			Commands =  new CommandGroup (
				new CommandGroup ("Menu",
					new ActionCommand ("New Game", () => loadWindow ("#ui.winNewGame.crow", this), "#icons.flag.svg"),
					new ActionCommand ("Save", () => loadWindow ("#ui.save.crow", this), "#icons.save.svg"),
					new ActionCommand ("Load", () => loadWindow ("#ui.winLoad.crow", this), "#icons.load.svg"),
					CMDOptions,
					CMDViewBoard,
					CMDViewMoves,
	#if DEBUG
					new ActionCommand ("Log", () => loadWindow ("#ui.winLog.crow", this), "#icons.load.svg"),
	#endif
					new ActionCommand ("Quit", () => Close(), "#icons.exit.svg")
				)
			);
		}


		public override string [] EnabledInstanceExtensions => new string [] {
#if DEBUG
			//Ext.I.VK_EXT_debug_utils,
#endif
		};

		public override string [] EnabledDeviceExtensions => new string [] {
			Ext.D.VK_KHR_swapchain,
			//Ext.D.VK_KHR_multiview
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
			if (!available_features.tessellationShader)
				EnableTesselation = false;
			enabled_features.tessellationShader = EnableTesselation;
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

		Queue transferQ;
		GraphicPipeline plToneMap;
		DescriptorSetLayout dslToneMap;
		bool updateRendererDescriptors;
		public VkSampleCountFlags AvailableSampleCount => ~phy.Limits.framebufferColorSampleCounts;
		public VkSampleCountFlags maxSampleCount {
			get {
				int sampleMask = (int)phy.Limits.framebufferColorSampleCounts;
				for (int i = 6; i > 0; i--)
					if ((sampleMask >> i) > 0)
						return (VkSampleCountFlags)Math.Pow(2, i);
				return VkSampleCountFlags.SampleCount1;
			}
		}
		protected override void createQueues () {
			base.createQueues ();
			transferQ = new Queue (dev, VkQueueFlags.Transfer);
		}
		public VkFormat GetSuitableGBuffImgFormat (params VkFormat[] formats) {
			foreach (VkFormat f in formats) {
				if (phy.GetFormatProperties (f).optimalTilingFeatures.HasFlag(VkFormatFeatureFlags.ColorAttachment | VkFormatFeatureFlags.SampledImage))
					return f;
			}
			throw new InvalidOperationException ("No suitable image format for the GBuffer found.");
		}
		protected override void initVulkan () {
			initLog ();
			initCommands();

			base.initVulkan ();

			iFace.SetWindowIcon ("#Crow.Icons.crow.png");

			VkFormat gbuffFormat = LowerGBuffFormat ?
				GetSuitableGBuffImgFormat (VkFormat.R16g16b16a16Sfloat) :
				GetSuitableGBuffImgFormat (VkFormat.R64g64b64a64Sfloat, VkFormat.R32g32b32a32Sfloat, VkFormat.R16g16b16a16Sfloat);

			DeferredPbrRendererBase.MRT_FORMAT = gbuffFormat;
			DeferredPbrRendererBase.HDR_FORMAT = gbuffFormat;


			VkSampleCountFlags maxSamples = maxSampleCount;
			if (SampleCount > maxSamples)
				SampleCount = maxSamples;
			DeferredPbrRendererBase.NUM_SAMPLES =  SampleCount;

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

			cmds = cmdPool.AllocateCommandBuffer (swapChain.ImageCount);

			createCamera ();

			renderer = new DeferredPbrRenderer (dev, swapChain, presentQueue, cubemapPathes [0], camera.NearPlane, camera.FarPlane);

			renderer.matrices.scaleIBLAmbient = IBLAmbient;
			renderer.lights[0].color = new Vector4 (LightStrength);
			renderer.LoadModel (transferQ, "data/models/chess.glb");
			//renderer.LoadModel (transferQ, "/mnt/devel/vkChess.net/data/models/chess3.glb");
			// Matrix4x4.CreateScale(1f / Math.Max(Math.Max(renderer.modelAABB.Width, renderer.modelAABB.Height), renderer.modelAABB.Depth));

			if (EnableReflections) {
				DescriptorSetWrites dsw = new DescriptorSetWrites (descriptorSet,
					dslToneMap.Bindings[4]);
				dsw.Write (dev, renderer.uboMatrices.Descriptor);
			}

			UpdateFrequency = 5;

			curRenderer = renderer;

			updateCellHighlighting();

			initBoard ();
			initStockfish ();

			iFace.Load ("#ui.menuOverlay.crow").DataSource = this;
			iFace.Load ("#ui.chess.crow").DataSource = this;

			loadGameFromPositionString (Configuration.Global.Get<string> ("CurrentGame"));

			restoreSavedOpenedWindows ();
		}
		void createCamera () {
			camera = new Camera (Utils.DegreesToRadians (CameraFOV), 1f, 0.1f, 32f);
			camera.SetPosition (CameraPosition.X, CameraPosition.Y, -CameraPosition.Z);
			camera.SetRotation (CameraRotation.X, CameraRotation.Y, CameraRotation.Z);
			camera.AspectRatio = Width / Height;
			camera.Model = Matrix4x4.CreateScale (0.5f);
		}

		#region VKCrowWindow pipeline overrides
		protected override void CreateRenderPass () {
			renderPass = new RenderPass (dev, swapChain.ColorFormat, DeferredPbrRendererBase.NUM_SAMPLES);
		}
		protected override void CreateAndAllocateDescriptors () {
			descriptorPool = EnableReflections ?
				new DescriptorPool (dev, 1,
					new VkDescriptorPoolSize (VkDescriptorType.CombinedImageSampler, 4),
					new VkDescriptorPoolSize (VkDescriptorType.UniformBuffer, 1)) :
				new DescriptorPool (dev, 1,
					new VkDescriptorPoolSize (VkDescriptorType.CombinedImageSampler, 2));
			descriptorSet = descriptorPool.Allocate (dslToneMap);
		}
		protected override void CreatePipeline () {
			using (GraphicPipelineConfig cfg = GraphicPipelineConfig.CreateDefault (VkPrimitiveTopology.TriangleList, DeferredPbrRendererBase.NUM_SAMPLES)) {
				if (DeferredPbrRendererBase.NUM_SAMPLES != VkSampleCountFlags.SampleCount1) {
					cfg.multisampleState.sampleShadingEnable = true;
					cfg.multisampleState.minSampleShading = 0.5f;
				}
				dslToneMap = new DescriptorSetLayout (dev, 0,
					new VkDescriptorSetLayoutBinding (0, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),
					new VkDescriptorSetLayoutBinding (1, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler)
				);
				if (EnableReflections) {
					dslToneMap.Bindings.AddRange ( new VkDescriptorSetLayoutBinding[] {
						new VkDescriptorSetLayoutBinding (2, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),
						new VkDescriptorSetLayoutBinding (3, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),
						new VkDescriptorSetLayoutBinding (4, VkShaderStageFlags.Fragment, VkDescriptorType.UniformBuffer)
					});
					cfg.Layout = new PipelineLayout (dev,
						new VkPushConstantRange (VkShaderStageFlags.Fragment, 20u), dslToneMap);
				} else
					cfg.Layout = new PipelineLayout (dev,
						new VkPushConstantRange (VkShaderStageFlags.Fragment, 8u), dslToneMap);

				cfg.RenderPass = renderPass;
				cfg.AddShaders (
					new ShaderInfo (dev, VkShaderStageFlags.Vertex, "#vke.FullScreenQuad.vert.spv"),
					new ShaderInfo (dev, VkShaderStageFlags.Fragment,
						EnableReflections ?
							"#vkChess.net.tone_mapping_ssr.frag.spv" : "#vkChess.net.tone_mapping.frag.spv")
				);

				plToneMap = new GraphicPipeline (cfg);

			}
		}
		protected override void buildCommandBuffer (PrimaryCommandBuffer cmd, int imageIndex) {
			if (updateRendererDescriptors) {
				if (EnableReflections) {
					DescriptorSetWrites dsw = new DescriptorSetWrites (descriptorSet,
						dslToneMap.Bindings[1], dslToneMap.Bindings[2], dslToneMap.Bindings[3]);
					dsw.Write (dev, renderer.HDROutput.Descriptor, renderer.GBuffPosDepthOutput.Descriptor, renderer.GBuffN_AO.Descriptor);
				} else {
					DescriptorSetWrites dsw = new DescriptorSetWrites (descriptorSet,	dslToneMap.Bindings[1]);
					dsw.Write (dev, renderer.HDROutput.Descriptor);
				}
				updateRendererDescriptors = false;
			}

			renderer?.recordDraw (cmd, imageIndex, instanceBuff, instancedCmds?.ToArray ());


			renderPass.Begin(cmd, frameBuffers[imageIndex]);

			cmd.SetViewport (frameBuffers[imageIndex].Width, frameBuffers[imageIndex].Height);
			cmd.SetScissor (frameBuffers[imageIndex].Width, frameBuffers[imageIndex].Height);

			if (EnableReflections) {
				cmd.PushConstant (plToneMap.Layout, VkShaderStageFlags.Fragment, 16, new float[] { Exposure, Gamma, SSRStep, SSRThreshold }, 0);
				cmd.PushConstant (plToneMap.Layout, VkShaderStageFlags.Fragment, 4, SSRMaxStepCount, 16);
			} else
				cmd.PushConstant (plToneMap.Layout, VkShaderStageFlags.Fragment, 16, new float[] { Exposure, Gamma}, 0);

			plToneMap.BindDescriptorSet (cmd, descriptorSet);
			plToneMap.Bind (cmd);
			cmd.Draw (3, 1, 0, 0);

			renderPass.End (cmd);
		}
		#endregion


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

		public override void UpdateView () {
			dev.WaitIdle ();
			if (updateViewRequested) {
				renderer.UpdateView (camera);
				updateViewRequested = false;
			}
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

			//adapt animation step on current fps
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
			renderer.Resize ();

			updateRendererDescriptors = true;
			base.OnResize ();

			updateViewRequested = true;
		}


		protected override void Dispose (bool disposing) {
			saveCurrentGame ();
			saveOpenedWindowConfig ();
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
			if (iFace.OnMouseMove ((int)xPos, (int)yPos))
				return;

			double diffX = lastMouseX - xPos;
			double diffY = lastMouseY - yPos;

			if (GetButton (MouseButton.Right) == InputAction.Press) {
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
				NotifyValueChanged ("DebugCurCell", (object)getChessCell (p.BoardCell.X, p.BoardCell.Y));
			}else
				NotifyValueChanged ("DebugCurCell", (object)"");
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
						promotePosition = Selection;
						showPromoteDialog ();
						return;
					} else
						processMove (getChessCell (Active) + getChessCell (Selection));

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
				saveGame ();
				break;
			case Glfw.Key.F3:
				checkBoardIntegrity ();
				break;
			case Glfw.Key.F4:
				loadWindow (@"ui/winLoad.crow", this);
				break;
			case Glfw.Key.Keypad0:
				whitePieces [0].X = 0;
				whitePieces [0].Y = 0;
				break;
			case Glfw.Key.Keypad6:
				whitePieces [0].X++;
				break;
			case Glfw.Key.Keypad8:
				whitePieces [0].Y++;
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
				if (currentState < GameState.Pad || (!playerIsAi (CurrentPlayer) && playerIsAi (Opponent)))
					undoLastMove ();

				undoLastMove ();

				startTurn ();
				NotifyValueChanged ("board", board);
				NotifyValueChanged ("CurrentState", currentState);
				NotifyValueChanged ("CurrentStateColor", CurrentStateColor);

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
		void onNewWhiteGame (object sender, MouseButtonEventArgs e) {
			closeWindow ("#ui.winNewGame.crow");
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

			Animation.StartAnimation (new AngleAnimation (this, "CameraAngleY", 0, 50));
		}
		void onNewBlackGame (object sender, MouseButtonEventArgs e) {
			closeWindow ("#ui.winNewGame.crow");
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

			Animation.StartAnimation (new AngleAnimation (this, "CameraAngleY", MathHelper.Pi, 50));
		}
		void onPromoteToQueenClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active) + getChessCell (promotePosition) + "q");
			promotePosition = -1;
			if (enableHint)
				sendToStockfish ("stop");
			else
				switchPlayer ();
		}
		void onPromoteToBishopClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active) + getChessCell (promotePosition) + "b");
			promotePosition = -1;
			if (enableHint)
				sendToStockfish ("stop");
			else
				switchPlayer ();
		}
		void onPromoteToRookClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active) + getChessCell (promotePosition) + "r");
			promotePosition = -1;
			if (enableHint)
				sendToStockfish ("stop");
			else
				switchPlayer ();
		}
		void onPromoteToKnightClick (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/promote.crow");
			processMove (getChessCell (Active) + getChessCell (promotePosition) + "k");
			promotePosition = -1;
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
		public bool RestartRequired => false;
		public VkSampleCountFlags SampleCount {
			get => Configuration.Global.Get<VkSampleCountFlags> ("SampleCount", VkSampleCountFlags.SampleCount1);
			set {
				if (value == SampleCount)
					return;
				Configuration.Global.Set ("SampleCount", value);
				DeferredPbrRendererBase.NUM_SAMPLES = value;
				NotifyValueChanged ("SampleCount", value);
				NotifyValueChanged ("RestartRequired", true);
			}
		}
		public bool LowerGBuffFormat {
			get => Configuration.Global.Get<bool> ("LowerGBuffFormat", true);
			set {
				if (value == LowerGBuffFormat)
					return;
				Configuration.Global.Set ("LowerGBuffFormat", value);
				NotifyValueChanged ("LowerGBuffFormat", value);
				NotifyValueChanged ("RestartRequired", true);
			}
		}
		public bool EnableTesselation {
			get => Configuration.Global.Get<bool> ("EnableTesselation", false);
			set {
				if (value == EnableTesselation)
					return;
				Configuration.Global.Set ("EnableTesselation", value);
				NotifyValueChanged ("EnableTesselation", value);
				NotifyValueChanged ("RestartRequired", true);
			}
		}
		public bool EnableReflections {
			get => Configuration.Global.Get<bool> ("EnableReflections", false);
			set {
				if (value == EnableReflections)
					return;
				Configuration.Global.Set ("EnableReflections", value);
				NotifyValueChanged ("EnableReflections", value);
				NotifyValueChanged ("RestartRequired", true);
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

		public Vector3 CameraRotation {
			get => ExtensionMethods.ParseVec3 (Configuration.Global.Get<string> ("CameraRotation", "<0.6,0,0>"));
			set {
				if (value == CameraRotation)
					return;

				Vector3 tmp = new Vector3 (
					MathHelper.NormalizeAngle (value.X),
					MathHelper.NormalizeAngle (value.Y),
					MathHelper.NormalizeAngle (value.Z)
				);
				Configuration.Global.Set ("CameraRotation", tmp.ToString());
				NotifyValueChanged ("CameraRotation", tmp);
				camera.SetRotation (tmp.X, tmp.Y, tmp.Z);
				updateViewRequested = true;
			}
		}
		public float CameraAngleY {
			get => CameraRotation.Y;
			set => CameraRotation = new Vector3(CameraRotation.X, value, CameraRotation.Z);
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
		public Vector3 BlackColor {
			get => ExtensionMethods.ParseVec3 (Configuration.Global.Get<string> ("BlackColor", "<0.03, 0.03, 0.03>"));
			set {
				if (value == BlackColor)
					return;

				Configuration.Global.Set ("BlackColor", value.ToString());
				NotifyValueChanged ("BlackColor", value);

				foreach	(Piece p in blackPieces)
					p.SetColor (value);
			}
		}
		public Vector3 WhiteColor {
			get => ExtensionMethods.ParseVec3 (Configuration.Global.Get<string> ("WhiteColor", "<1, 1, 1>"));
			set {
				if (value == WhiteColor)
					return;

				Configuration.Global.Set ("WhiteColor", value.ToString());
				NotifyValueChanged ("WhiteColor", value);

				foreach	(Piece p in whitePieces)
					p.SetColor (value);
			}
		}
		public bool OptColorIsExpanded {
			get => Configuration.Global.Get ("OptColorIsExpanded", false);
			set {
				if (OptColorIsExpanded == value)
					return;
				Configuration.Global.Set ("OptColorIsExpanded", value);
			}
		}
		public bool OptRenderingIsExpanded {
			get => Configuration.Global.Get ("OptRenderingIsExpanded", false);
			set {
				if (OptRenderingIsExpanded == value)
					return;
				Configuration.Global.Set ("OptRenderingIsExpanded", value);
			}
		}
		public bool OptCameraIsExpanded {
			get => Configuration.Global.Get ("OptCameraIsExpanded", false);
			set {
				if (OptCameraIsExpanded == value)
					return;
				Configuration.Global.Set ("OptCameraIsExpanded", value);
			}
		}
		public bool OptGameIsExpanded {
			get => Configuration.Global.Get ("OptGameIsExpanded", true);
			set {
				if (OptGameIsExpanded == value)
					return;
				Configuration.Global.Set ("OptGameIsExpanded", value);
			}
		}
		string defaultSaveDirectory => System.IO.Path.Combine (Configuration.AppConfigPath, "SavedGames");
		public string[] SavedGames {
			get {
				if (!Directory.Exists (defaultSaveDirectory))
					return null;
				return Directory.GetFiles (defaultSaveDirectory, "*.chess");
			}
		}
		string selectedSavedGame;
		public string SelectedSavedGame {
			get => selectedSavedGame;
			set {
				if (selectedSavedGame == value)
					return;
				selectedSavedGame = value;
				NotifyValueChanged (selectedSavedGame);

				if (string.IsNullOrEmpty (selectedSavedGame))
					CMDLoadSelectedFile.CanExecute = false;
				else if (File.Exists (selectedSavedGame))
					CMDLoadSelectedFile.CanExecute = true;
			}
		}
		void saveCurrentGame() {
			if (historyIdx > 0)
				StockfishMoves = savedMoves;
			Configuration.Global.Set ("CurrentGame", stockfishPositionCommand);
		}

		void loadGameFromPositionString (string positions) {
			if (string.IsNullOrEmpty (positions))
				return;

			stockfishMoves = new ObservableList<string> (positions.Split (' '));

			if (currentState < GameState.Play)
				return;

			resync3DScene ();
		}
		void saveGame (string fileName = null) {
			if (!Directory.Exists (defaultSaveDirectory))
				Directory.CreateDirectory (defaultSaveDirectory);

			if (string.IsNullOrEmpty (fileName))
				fileName = $"{DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss")}.chess";
			else if (!fileName.EndsWith (".chess"))
				fileName += ".chess";

			string filePath = System.IO.Path.Combine (defaultSaveDirectory, fileName);
			using (Stream stream = new FileStream (filePath, FileMode.Create))
				using (StreamWriter sw = new StreamWriter (stream))
					sw.WriteLine (stockfishPositionCommand);
		}
		void loadGameFromFile (string fullPath) {
			closeWindow ("#ui.winBoard.crow");
			if (string.IsNullOrEmpty (fullPath) && !File.Exists (fullPath))
				return;
			using (Stream stream = new FileStream (fullPath, FileMode.Open))
				using (StreamReader sw = new StreamReader (stream))
					loadGameFromPositionString (sw.ReadLine ());
		}
		void saveOpenedWindowConfig () {
			StringBuilder sb = new StringBuilder(100);
			foreach	(Window win in iFace.GraphicTree.OfType<Window>())
				sb.Append ($"{win.Name};{win.Left};{win.Top};{win.Width};{win.Height};{win.HorizontalAlignment};{win.VerticalAlignment}|");
			Configuration.Global.Set<string> ("OpenedWindows",
				sb.Length > 0 ?	sb.ToString (0, sb.Length - 1) : "");
		}
		void restoreSavedOpenedWindows () {
			string wins = Configuration.Global.Get<string> ("OpenedWindows");
			if (string.IsNullOrEmpty (wins))
				return;
			foreach	(string w in wins.Split ('|')) {
				string [] tmp = w.Split (';');
				loadWindow (tmp[0], this);
				Widget win = iFace.FindByName (tmp[0]);
				win.Left = int.Parse (tmp[1]);
				win.Top = int.Parse (tmp[2]);
				win.Width = Measure.Parse (tmp[3]);
				win.Height = Measure.Parse (tmp[4]);
				win.HorizontalAlignment = EnumsNET.Enums.Parse<HorizontalAlignment> (tmp[5]);
				win.VerticalAlignment = EnumsNET.Enums.Parse<VerticalAlignment> (tmp[6]);
			}
		}
		#endregion

		#region LOGS
		public CommandGroup LogContextMenu;
		ObservableList<LogEntry> logs = new ObservableList<LogEntry>();
		public ObservableList<LogEntry> MainLog => logs;
		void initLog () {
			LogContextMenu = new CommandGroup (new ActionCommand("Clear Log", () => ResetLog()));
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

		#region move history show
		int historyIdx = 0;
		public int HistoryMoveIndex {
			get => historyIdx;
			set {
				if  (historyIdx == value)
					return;

				if (historyIdx == 0) {
					//save current positions
					lock (movesMutex)
						savedMoves = StockfishMoves;
				}

				historyIdx = value;
				NotifyValueChanged (historyIdx);

				if (historyIdx == 0) {
					//restore current saved positions
					lock (movesMutex)
						StockfishMoves = savedMoves;
					savedMoves = null;
					resync3DScene ();
					startTurn ();
				} else {
					lock (movesMutex)
						StockfishMoves = savedMoves.Take (savedMoves.Count - historyIdx).ToList();
					CurrentState = GameState.Play;
					resync3DScene ();
				}
			}
		}

		IList<string> savedMoves;


		#endregion

		#region Stockfish
		public static object movesMutex = new object ();
		Process stockfish;
		volatile bool waitAnimationFinished;
		//Queue<string> stockfishCmdQueue = new Queue<string> ();
		IList<String> stockfishMoves = new ObservableList<string> ();

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
				NotifyValueChanged (value);

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

		public IList<String> StockfishMoves {
			get => stockfishMoves;
			set { stockfishMoves = value; }
		}
		void resync3DScene () {
			replaySilently ();

			foreach (Piece p in whitePieces)
				p.MoveTo (p.BoardCell, true);
			foreach (Piece p in blackPieces)
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

			if (string.IsNullOrEmpty (StockfishPath)) {
				if (RuntimeInformation.IsOSPlatform (OSPlatform.Linux))
					StockfishPath = "./stockfish_14_x64";
				else if (RuntimeInformation.IsOSPlatform (OSPlatform.Windows)) {
					if (RuntimeInformation.OSArchitecture == Architecture.X64)
						StockfishPath = "stockfish_14_x64.exe";
					else if (RuntimeInformation.OSArchitecture == Architecture.X86)
						StockfishPath = "stockfish_14_32bit.exe";
				}
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
					NotifyValueChanged("StockfishVersion", (object)tmp[1]);
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
		Piece [] whitePieces;
		Piece [] blackPieces;
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

				if (currentState > GameState.Pad) {
					Point kPos = CurrentPlayerPieces.First (p => p.Type == PieceType.King).BoardCell;
					Piece.UpdateCase (kPos.X, kPos.Y, kingCheckedColor);
					if (currentState == GameState.Checkmate) {
						Piece king = CurrentPlayerPieces.First (p => p.Type == PieceType.King);
						Animation.StartAnimation (new FloatAnimation2 (king, "Z", 0.4f, 20));
						Animation.StartAnimation (new AngleAnimation (king, "XAngle", MathHelper.Pi * 0.55f));
						Animation.StartAnimation (new AngleAnimation (king, "ZAngle", king.ZAngle + 0.3f));
					}
				}

				NotifyValueChanged ("CurrentState", currentState);
				NotifyValueChanged ("CurrentStateColor", CurrentStateColor);
			}
		}
		Color CurrentStateColor {
			get {
				switch (currentState) {
					case GameState.Play:
						return Colors.DarkGrey;
					case GameState.Pad:
						return Colors.DarkMagenta;
					case GameState.Checked:
						return Colors.DarkOrange;
					case GameState.Checkmate:
						return Colors.DarkRed;
				}
				return Colors.DarkSlateGrey;
			}
		}
		ChessColor currentPlayer;
		Point promotePosition = -1;

		public ChessColor CurrentPlayer {
			get => currentPlayer;
			set {
				if (currentPlayer == value)
					return;
				currentPlayer = value;
				NotifyValueChanged ("CurrentPlayer", currentPlayer);
			}
		}

		public ChessColor Opponent =>
			CurrentPlayer == ChessColor.White ? ChessColor.Black : ChessColor.White;
		public Piece [] OpponentPieces =>
			CurrentPlayer == ChessColor.White ? blackPieces : whitePieces;
		public Piece [] CurrentPlayerPieces
			=> CurrentPlayer == ChessColor.White ? whitePieces : blackPieces;
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
					NotifyValueChanged ("ActCell", (object)"");
					return;
				}

				NotifyValueChanged ("ActCell", (object)getChessCell (active.X, active.Y));
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
				NotifyValueChanged ("SelCell", (object)getChessCell (selection.X, selection.Y));
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

			Active = -1;

			board = new Piece [8, 8];
			instanceBuff = new HostBuffer<InstanceData> (dev, VkBufferUsageFlags.VertexBuffer, 98, true, false);
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

			whitePieces = board.Cast<Piece> ().Where (p => p?.Player == ChessColor.White).ToArray ();
			blackPieces = board.Cast<Piece> ().Where (p => p?.Player == ChessColor.Black).ToArray ();

			Piece.boardDatas = new InstanceData [64];

			Model.Node boardNode = renderer.model.FindNode ("Decorations");

			uint curInstIdx = 32;
			boardCellMesheIndices = new int [64 + boardNode.Children.Count];

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
			for (int i = 0; i < boardNode.Children.Count; i++) {
				boardCellMesheIndices [64 + i] = renderer.model.Meshes.IndexOf (boardNode.Children[i].Mesh);
				instanceBuff.Update (96 + (uint)i, new InstanceData (new Vector4 (1f), Matrix4x4.Identity));
			}
			/*boardCellMesheIndices [64] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("frame").Mesh);
			instanceBuff.Update (96, new InstanceData (new Vector4 (0.2f,0.2f,0.2f,1f), Matrix4x4.Identity));
			boardCellMesheIndices [65] = renderer.model.Meshes.IndexOf (renderer.model.FindNode ("decorations").Mesh);
			instanceBuff.Update (97, new InstanceData (new Vector4 (1), Matrix4x4.CreateTranslation(0,0.001f,0)));*/
			Piece.flushEnd = 96 + (uint)boardNode.Children.Count;

			updateDrawCmdList ();
			currentState = GameState.SceneInitialized;
		}

		void updateDrawCmdList () {
			List<Model.InstancedCmd> primitiveCmds = new List<Model.InstancedCmd> ();
			Piece [] pieces = whitePieces.Concat (blackPieces).ToArray ();

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

			Active = -1;
			board = new Piece [8, 8];

			foreach (Piece p in whitePieces) {
				p.Reset (animate);
				board [p.initX, p.initY] = p;
			}
			foreach (Piece p in blackPieces) {
				p.Reset (animate);
				board [p.initX, p.initY] = p;
			}
		}
		void addPiece (ChessColor player, PieceType _type, int col, int line) {
			Piece p = new Piece (player, _type, col, line,
				player == ChessColor.White ? WhiteColor : BlackColor);
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
			foreach (Piece p in whitePieces) {
				if (p.Captured)
					continue;
				if (board[p.BoardCell.X,p.BoardCell.Y] == p)
					continue;
				Console.WriteLine ($"incoherence for piece:{p.Player} {p.Type} has cell {p.BoardCell}");
				integrity = false;
			}
			foreach (Piece p in blackPieces) {
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
		string getChessCell (Point cell) => getChessCell (cell.X, cell.Y);
		public static string getChessCell (int col, int line) {
			if (col < 0 || line < 0)
				return null;
			char c = (char)(col + 97);
			return c.ToString () + (line + 1).ToString ();
		}
		public static Point getChessCell (ReadOnlySpan<char> s)
			=> new Point ((int)s [0] - 97, int.Parse (s [1].ToString ()) - 1);
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
				if (currentState == GameState.Checkmate) {
					Piece king = CurrentPlayerPieces.First (p => p.Type == PieceType.King);
					Animation.StartAnimation (new FloatAnimation2 (king, "Z", 0, 20));
					Animation.StartAnimation (new AngleAnimation (king, "XAngle", 0));
					Animation.StartAnimation (new AngleAnimation (king, "ZAngle", 0));
				}
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
				currentPlayer = Opponent;
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