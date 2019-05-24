using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Crow;
using CVKL;
using Glfw;
using VK;

namespace vkChess
{
	public enum GameState { Init, Play, Pad, Checked, Checkmate };
	public enum PlayerType { Human, AI };
	public enum ChessColor { White, Black };
	public enum PieceType { Pawn, Rook, Knight, Bishop, King, Queen };

	public class VkChess : CrowWin
	{
		static void Main (string [] args) {
			Instance.DEBUG_UTILS = false;
			Instance.VALIDATION = false;
			DeferredPbrRenderer.NUM_SAMPLES = VkSampleCountFlags.SampleCount4;
			DeferredPbrRenderer.DRAW_INSTACED = true;
			DeferredPbrRenderer.TEXTURE_ARRAY = true;

			//Instance.RenderDocCapture = true;

			using (VkChess app = new VkChess ())
				app.Run ();
		}

		protected override void configureEnabledFeatures (VkPhysicalDeviceFeatures available_features, ref VkPhysicalDeviceFeatures enabled_features) {
			base.configureEnabledFeatures (available_features, ref enabled_features);

			enabled_features.samplerAnisotropy = available_features.samplerAnisotropy;
			enabled_features.sampleRateShading = available_features.sampleRateShading;
			enabled_features.geometryShader = available_features.geometryShader;
			enabled_features.tessellationShader = available_features.tessellationShader;

			enabled_features.textureCompressionBC = available_features.textureCompressionBC;
		}

		Queue transferQ;
		protected override void createQueues () {
			base.createQueues ();
			transferQ = new Queue (dev, VkQueueFlags.Transfer);
		}

		string [] cubemapPathes = {
			"data/textures/papermill.ktx",
			"data/textures/cubemap_yokohama_bc3_unorm.ktx",
			"data/textures/gcanyon_cube.ktx",
			"data/textures/pisa_cube.ktx",
			"data/textures/uffizi_cube.ktx",
		};
		string [] modelPathes = {
			"data/models/chess.glb",
			"data/models/DamagedHelmet/glTF/DamagedHelmet.gltf",
			"data/models/Hubble.glb",
			"data/models/MER_static.glb",
			"data/models/ISS_stationary.glb",
		};

		DeferredPbrRenderer renderer;

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

		public static DeferredPbrRenderer curRenderer;

		protected override void onLoad () {
			Configuration.Global.Set ("StockfishPath", "/usr/games/stockfish");

			camera = new Camera (Utils.DegreesToRadians (45f), 1f, 0.1f, 32f);
			camera.SetRotation (0.6f, 0, 0);
			camera.SetPosition (0, 0f, 12.0f);

			renderer = new DeferredPbrRenderer (dev, swapChain, presentQueue, cubemapPathes [2], camera.NearPlane, camera.FarPlane);
			dev.WaitIdle ();
			renderer.LoadModel (transferQ, modelPathes [0]);
			camera.Model = Matrix4x4.CreateScale (0.5f);// Matrix4x4.CreateScale(1f / Math.Max(Math.Max(renderer.modelAABB.Width, renderer.modelAABB.Height), renderer.modelAABB.Depth));

			UpdateFrequency = 8;

			curRenderer = renderer;

			crow.Load ("ui/chess.crow").DataSource = this;

			initBoard ();
			initStockfish ();

			CurrentState = GameState.Play;
		}

		protected override void recordDraw (CommandBuffer cmd, int imageIndex) {
			renderer.recordDraw (cmd, imageIndex, instanceBuff, instancedCmds?.ToArray ());
		}

		public override void UpdateView () {
			renderer.UpdateView (camera);
			updateViewRequested = false;
			if (instanceBuff == null)
				return;
			if (renderer.shadowMapRenderer.updateShadowMap)
				renderer.shadowMapRenderer.update_shadow_map (cmdPool, instanceBuff, instancedCmds.ToArray ());
		}

		public static bool updateInstanceCmds = true;

		public override void Update () {
			if (updateInstanceCmds) {
				updateDrawCmdList ();
				rebuildBuffers = true;
			}
			if (Animation.HasAnimations)
				renderer.shadowMapRenderer.updateShadowMap = true;

			Animation.ProcessAnimations ();
			Piece.FlushHostBuffer ();

			if (instanceBuff != null && renderer.shadowMapRenderer.updateShadowMap)
				renderer.shadowMapRenderer.update_shadow_map (cmdPool, instanceBuff, instancedCmds.ToArray ());

			base.Update ();
		}

		protected override void OnResize () {
			renderer.Resize ();
			base.OnResize ();
		}


		protected override void Dispose (bool disposing) {
			if (disposing) {
				if (!isDisposed) {
					renderer.Dispose ();
					instanceBuff.Dispose ();
				}
			}

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
			if (KeyModifiers.HasFlag (Modifier.Shift)) {
				if (crow.ProcessMouseWheelChanged ((float)xOffset))
					return;
			} else if (crow.ProcessMouseWheelChanged ((float)yOffset))
				return;
			camera.Move (0, 0, (float)yOffset * -4f);
			updateViewRequested = true;
		}
		protected override void onMouseMove (double xPos, double yPos) {
			if (crow.ProcessMouseMove ((int)xPos, (int)yPos))
				return;

			double diffX = lastMouseX - xPos;
			double diffY = lastMouseY - yPos;
			if (MouseButton [1]) {
				camera.Rotate ((float)-diffX, (float)-diffY);
				updateViewRequested = true;
			}

			if (currentState == GameState.Init)
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
		protected override void onMouseButtonDown (Glfw.MouseButton button) {
			if (crow.ProcessMouseButtonDown ((Crow.MouseButton)button))
				return;

			//if (waitAnimationFinished) {
			//	base.onMouseButtonDown (button);
			//	return;
			//}

			if (currentState == GameState.Init)
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
				loadWindow (@"ui/main.crow", this);
				break;
			case Glfw.Key.F2:
				loadWindow (@"ui/board.crow", this);
				NotifyValueChanged ("board", board);
				//crow.Load (@"ui/scene.crow").DataSource = this;
				break;
			case Glfw.Key.F3:
				checkBoardIntegrity ();
				break;
			case Glfw.Key.S:
				syncStockfish ();
				sendToStockfish ("go");
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
				resetBoard (true);
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

		void onUndoClick (object sender, MouseButtonEventArgs e) {
			bool hintIsEnabled = EnableHint;
			EnableHint = false;

			GameState lastState = currentState;
			if ((currentState != GameState.Pad && currentState != GameState.Checkmate) || !playerIsAi (CurrentPlayer))//undo ai move
				undoLastMove ();			
			
			undoLastMove ();
			//to have checkColor removed
			currentState = lastState;
			CurrentState = GameState.Play;
			startTurn ();
			NotifyValueChanged ("board", board);

			EnableHint = hintIsEnabled;
		}
		void onNewGameClick (object sender, MouseButtonEventArgs e) {
			loadWindow ("ui/newGame.crow", this);
		}
		void onNewWhiteGame (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/newGame.crow");

			CurrentState = GameState.Play;

			WhitesAreAI = false;
			BlacksAreAI = true;

			resetBoard ();
			syncStockfish ();
		}
		void onNewBlackGame (object sender, MouseButtonEventArgs e) {
			closeWindow ("ui/newGame.crow");

			CurrentState = GameState.Play;

			WhitesAreAI = true;
			BlacksAreAI = false;

			resetBoard ();
			syncStockfish ();

			sendToStockfish ("go");
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
			get { return renderer.matrices.gamma; }
			set {
				if (value == renderer.matrices.gamma)
					return;
				renderer.matrices.gamma = value;
				NotifyValueChanged ("Gamma", value);
				updateViewRequested = true;
			}
		}
		public float Exposure {
			get { return renderer.matrices.exposure; }
			set {
				if (value == renderer.matrices.exposure)
					return;
				renderer.matrices.exposure = value;
				NotifyValueChanged ("Exposure", value);
				updateViewRequested = true;
			}
		}
		public float IBLAmbient {
			get { return renderer.matrices.scaleIBLAmbient; }
			set {
				if (value == renderer.matrices.scaleIBLAmbient)
					return;
				renderer.matrices.scaleIBLAmbient = value;
				NotifyValueChanged ("IBLAmbient", value);
				updateViewRequested = true;
			}
		}
		public float LightStrength {
			get { return renderer.lights [renderer.lightNumDebug].color.X; }
			set {
				if (value == renderer.lights [renderer.lightNumDebug].color.X)
					return;
				renderer.lights [renderer.lightNumDebug].color = new Vector4 (value);
				NotifyValueChanged ("LightStrength", value);
				renderer.uboLights.Update (renderer.lights);
			}
		}
		#endregion

		#region LOGS
		List<string> logBuffer = new List<string> ();
		void AddLog (string msg) {
			if (string.IsNullOrEmpty (msg))
				return;
			logBuffer.Add (msg);
			NotifyValueChanged ("LogBuffer", logBuffer);
			Console.WriteLine (msg);
		}
		#endregion

		#region Stockfish
		Process stockfish;
		volatile bool waitAnimationFinished;
		volatile bool stockfishIsReady;
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
					} else
						sendToStockfish ("stop");
				}

				NotifyValueChanged ("EnableHint", enableHint);
			}
		}
		public bool StockfishNotFound {
			get { return stockfish == null; }
		}
		public string StockfishPath {
			get { return Configuration.Global.Get<string> ("StockfishPath"); }
			set {
				if (value == StockfishPath)
					return;
				Configuration.Global.Set ("StockfishPath", value);
				NotifyValueChanged ("StockfishPath", value);

				initStockfish ();
			}
		}
		public int AISearchTime {
			get { return Configuration.Global.Get<int> ("AISearchTime"); }
			set {
				if (value == WhitesLevel)
					return;

				Configuration.Global.Set ("AISearchTime", value);
				NotifyValueChanged ("AISearchTime", value);
			}
		}
		public int WhitesLevel {
			get { return Configuration.Global.Get<int> ("WhitesLevel"); }
			set {
				if (value == WhitesLevel)
					return;

				Configuration.Global.Set ("WhitesLevel", value);
				NotifyValueChanged ("WhitesLevel", value);
			}
		}
		public int BlacksLevel {
			get { return Configuration.Global.Get<int> ("BlacksLevel"); }
			set {
				if (value == BlacksLevel)
					return;

				Configuration.Global.Set ("BlacksLevel", value);
				NotifyValueChanged ("BlacksLevel", value);
			}
		}
		public bool WhitesAreAI {
			get { return Configuration.Global.Get<bool> ("WhitesAreAI"); }
			set {
				if (value == WhitesAreAI)
					return;

				Configuration.Global.Set ("WhitesAreAI", value);
				NotifyValueChanged ("WhitesAreAI", value);
			}
		}
		public bool BlacksAreAI {
			get { return Configuration.Global.Get<bool> ("BlacksAreAI"); }
			set {
				if (value == BlacksAreAI)
					return;

				Configuration.Global.Set ("BlacksAreAI", value);
				NotifyValueChanged ("BlacksAreAI", value);
			}
		}
		string stockfishPositionCommand {
			get {
				string tmp = "position startpos moves ";
				return
					StockfishMoves.Count == 0 ? tmp : tmp + StockfishMoves.Aggregate ((i, j) => i + " " + j);
			}
		}

		public bool AutoPlayHint {
			get { return Configuration.Global.Get<bool> ("AutoPlayHint"); }
			set {
				if (value == AutoPlayHint)
					return;
				Crow.Configuration.Global.Set ("AutoPlayHint", value);
				NotifyValueChanged ("AutoPlayHint", value);
			}
		}

		public List<String> StockfishMoves {
			get { return stockfishMoves; }
			set { stockfishMoves = value; }
		}

		void initStockfish () {
			if (stockfish != null) {
				currentState = GameState.Play;
				resetBoard (false);

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

			sendToStockfish ("uci");
		}
		void syncStockfish () {
			NotifyValueChanged ("StockfishMoves", StockfishMoves);
			sendToStockfish ("setoption name Skill Level value " + (CurrentPlayer == ChessColor.White ? WhitesLevel.ToString() : BlacksLevel.ToString()));
			sendToStockfish (stockfishPositionCommand);
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
			AddLog ("<= " + msg);
			stockfish.WaitForInputIdle ();
			stockfish.StandardInput.WriteLine (msg);
		}
		void P_Exited (object sender, EventArgs e) {
			AddLog ("Stockfish Terminated");
		}
		void dataReceived (object sender, DataReceivedEventArgs e) {
			if (string.IsNullOrEmpty (e.Data))
				return;

			string [] tmp = e.Data.Split (' ');

			//if (tmp [0] != "readyok")
				AddLog ("=> " + e.Data);

			switch (tmp [0]) {
			case "readyok":
				stockfishIsReady = true;
				startTurn ();
				return;
			case "uciok":
				//NotifyValueChanged ("StockfishNotFound", false);
				//sendToStockfish ("setoption name Skill Level value " + StockfishLevel.ToString ());
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
				if (tmp [1] == "(none)") {
					if (checkKingIsSafe ())
						CurrentState = GameState.Pad;
					else
						CurrentState = GameState.Checkmate;
					return;
				}

				if (playerIsAi (CurrentPlayer)) {
					processMove (tmp [1]);
					switchPlayer ();
				} else if (enableHint)
					switchPlayer ();

				break;
			}
		}

		#endregion

		#region game logic
		public const int animationSteps = 50;
		static Vector4 bestMovePosColor = new Vector4 (0, 10, 10, 0);
		static Vector4 selectionPosColor = new Vector4 (0, 0, 5, 0);
		static Vector4 validPosColor = new Vector4 (0, 5, 0, 0);
		static Vector4 activeColor = new Vector4 (0, 5, 0, 0);
		static Vector4 kingCheckedColor = new Vector4 (20, 0, 0, 0);

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
			get { return bestMove; }
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
			get { return currentState; }
			set {
				if (currentState == value)
					return;

				if (currentState == GameState.Checked) {
					Point kPos = CurrentPlayerPieces.First (p => p.Type == PieceType.King).BoardCell;
					Piece.UpdateCase (kPos.X, kPos.Y, -kingCheckedColor);
				} else if (currentState == GameState.Checkmate) {
					Point kPos = OpponentPieces.First (p => p.Type == PieceType.King).BoardCell;
					Piece.UpdateCase (kPos.X, kPos.Y, -kingCheckedColor);
				}
				currentState = value;

				if ((int)currentState > 2) {
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

		public ChessColor CurrentPlayer;

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
			CurrentPlayer = ChessColor.White;
			cptWhiteOut = 0;
			cptBlackOut = 0;
			StockfishMoves.Clear ();
			NotifyValueChanged ("StockfishMoves", StockfishMoves);

			Active = -1;

			board = new Piece [8, 8];
			instanceBuff = new HostBuffer<InstanceData> (dev, VkBufferUsageFlags.VertexBuffer, 97, true, false);
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
				AddLog ("Previewing: " + move);
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

			p.Captured = true;
			p.HasMoved = true;

			if (animate)
				Animation.StartAnimation (new PathAnimation (p, "Position",
					new BezierPath (
					p.Position,
					capturePos, Vector3.UnitZ), animationSteps));
			else
				p.Position = capturePos;
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
				AddLog ("ERROR: impossible move.");
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

//			syncStockfish ();

			//animate undo capture
			if (pCaptured == null)
				return;
			Vector3 pCapLastPos = new Vector3 (pCaptured.BoardCell.X, pCaptured.BoardCell.Y, 0);
			//pCaptured.Position = getCurrentCapturePosition (pCaptured);

			Animation.StartAnimation (new PathAnimation (pCaptured, "Position",
				new BezierPath (
				pCaptured.Position,
				pCapLastPos, Vector3.UnitZ),animationSteps));
				
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

			if (playerIsAi (CurrentPlayer))
				sendToStockfish ("go movetime " + AISearchTime.ToString());
			else if (enableHint)
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