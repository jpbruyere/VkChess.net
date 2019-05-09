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

namespace vkChess {
    public enum GameState { Init, Play, Checked, Pad, Checkmate };
    public enum PlayerType { Human, AI };
    public enum ChessColor { White, Black };
    public enum PieceType { Pawn, Rook, Knight, Bishop, King, Queen };


    public class VkChess : CrowWin {
        static void Main(string[] args) {
            //Instance.DebugUtils = true;
            //Instance.Validation = true;
            //Instance.RenderDocCapture = true;

            using (VkChess app = new VkChess())
                app.Run();
        }

        protected override void configureEnabledFeatures(VkPhysicalDeviceFeatures available_features, ref VkPhysicalDeviceFeatures enabled_features) {
            base.configureEnabledFeatures(available_features, ref enabled_features);

            enabled_features.samplerAnisotropy = available_features.samplerAnisotropy;
            enabled_features.sampleRateShading = available_features.sampleRateShading;
            enabled_features.geometryShader = available_features.geometryShader;

            enabled_features.textureCompressionBC = available_features.textureCompressionBC;
        }

        Queue transferQ;
        protected override void createQueues() {
            base.createQueues();
            transferQ = new Queue(dev, VkQueueFlags.Transfer);
        }

        string[] cubemapPathes = {
            "data/textures/papermill.ktx",
            "data/textures/cubemap_yokohama_bc3_unorm.ktx",
            "data/textures/gcanyon_cube.ktx",
            "data/textures/pisa_cube.ktx",
            "data/textures/uffizi_cube.ktx",
        };
        string[] modelPathes = {
                "data/models/chess.glb",
                "data/models/DamagedHelmet/glTF/DamagedHelmet.gltf",
                "data/models/Hubble.glb",
                "data/models/MER_static.glb",
                "data/models/ISS_stationary.glb",
            };

        DeferredPbrRenderer renderer;

        public struct InstanceData {
            public Vector4 color;
            public Matrix4x4 mat;

            public InstanceData (Vector4 color, Matrix4x4 mat) {
                this.color = color;
                this.mat = mat;
            }
        }

        public HostBuffer<InstanceData> instanceBuff;
        Model.InstancedCmd[] instancedCmds;

		public static DeferredPbrRenderer curRenderer;

        protected override void onLoad() {
            Configuration.Global.Set("StockfishPath", "/usr/games/stockfish");

            camera = new Camera(Utils.DegreesToRadians(45f), 1f, 0.1f, 32f);
            camera.SetRotation(0.7f, 0, 0);
            camera.SetPosition(0, 0f, 10.0f);

            DeferredPbrRenderer.NUM_SAMPLES = VkSampleCountFlags.SampleCount4;
            DeferredPbrRenderer.DRAW_INSTACED = true;
            DeferredPbrRenderer.EnableTextureArray = true;

            renderer = new DeferredPbrRenderer(dev, swapChain, presentQueue, cubemapPathes[0], camera.NearPlane, camera.FarPlane);
            renderer.LoadModel(transferQ, modelPathes[0]);
			camera.Model = Matrix4x4.CreateScale (0.5f);// Matrix4x4.CreateScale(1f / Math.Max(Math.Max(renderer.modelAABB.Width, renderer.modelAABB.Height), renderer.modelAABB.Depth));


			UpdateFrequency = 10;

			curRenderer = renderer;
		}

        protected override void recordDraw(CommandBuffer cmd, int imageIndex) {
            renderer.recordDraw(cmd, imageIndex, instanceBuff, instancedCmds?.ToArray());
        }

        public override void UpdateView() {
            renderer.UpdateView(camera);
            updateViewRequested = false;
            if (instanceBuff == null)
                return;
            if (renderer.shadowMapRenderer.updateShadowMap)
                renderer.shadowMapRenderer.update_shadow_map(cmdPool, instanceBuff, instancedCmds.ToArray());
        }
        public override void Update() {
            updateChess();
            Animation.ProcessAnimations();
            Piece.FlushHostBuffer();
            base.Update();
        }
        protected override void OnResize() {
            renderer.Resize();
            base.OnResize();
        }


        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (!isDisposed) {
                    renderer.Dispose();
                    instanceBuff.Dispose();
                }
            }

            base.Dispose(disposing);
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
		Vector3 vEye;
		Vector3 vMouseRay;
		Vector3 vMouse;
		Vector3 target;

		protected override void onMouseMove(double xPos, double yPos) {
            base.onMouseMove(xPos, yPos);

            vMouse = UnProject(ref renderer.matrices.projection, ref renderer.matrices.view,
                swapChain.Width, swapChain.Height, new Vector2((float)xPos, (float)yPos)).ToVector3();

			Matrix4x4 invView;
			Matrix4x4.Invert (renderer.matrices.view, out invView);
			vEye = new Vector3 (invView.M41, invView.M42, invView.M43);
			vMouseRay = Vector3.Normalize (vMouse - vEye);

			float t = vMouse.Y / vMouseRay.Y;
			target = vMouse - vMouseRay * t;

			Point newPos = new Point((int)Math.Truncate(target.X+4), (int)Math.Truncate(4f-target.Z));
			Console.WriteLine (newPos);
            Selection = newPos;            
		}

		protected override void onKeyDown(Glfw.Key key, int scanCode, Modifier modifiers) {
            switch (key) {
                case Glfw.Key.F1:
                    crow.Load(@"ui/main.crow").DataSource = this;
                    break;
                case Glfw.Key.F2:
                    crow.Load(@"ui/scene.crow").DataSource = this;
                    break;
                case Glfw.Key.S:
                    syncStockfish();
                    sendToStockfish("go");
                    break;
                case Glfw.Key.Keypad0:
                    whites[0].X = 0;
                    whites[0].Y = 0;
                    break;
                case Glfw.Key.Keypad6:
                    whites[0].X++;
                    break;
                case Glfw.Key.Keypad8:
                    whites[0].Y++;
                    break;
                case Glfw.Key.R:
                    resetBoard(true);
                    break;
				case Glfw.Key.Enter:
					plDebugDraw.UpdateLine (4, Vector3.Zero, vMouse, 1, 0, 1);
					plDebugDraw.UpdateLine (5, Vector3.Zero, vEye, 1, 1, 0);
					plDebugDraw.UpdateLine (6, vMouse, target, 1, 1, 1);
					break;
                default:
                    base.onKeyDown(key, scanCode, modifiers);
                    break;
            }
        }

        #region crow
        public float Gamma {
            get { return renderer.matrices.gamma; }
            set {
                if (value == renderer.matrices.gamma)
                    return;
                renderer.matrices.gamma = value;
                NotifyValueChanged("Gamma", value);
                updateViewRequested = true;
            }
        }
        public float Exposure {
            get { return renderer.matrices.exposure; }
            set {
                if (value == renderer.matrices.exposure)
                    return;
                renderer.matrices.exposure = value;
                NotifyValueChanged("Exposure", value);
                updateViewRequested = true;
            }
        }
        public float IBLAmbient {
            get { return renderer.matrices.scaleIBLAmbient; }
            set {
                if (value == renderer.matrices.scaleIBLAmbient)
                    return;
                renderer.matrices.scaleIBLAmbient = value;
                NotifyValueChanged("IBLAmbient", value);
                updateViewRequested = true;
            }
        }
        public float LightStrength {
            get { return renderer.lights[renderer.lightNumDebug].color.X; }
            set {
                if (value == renderer.lights[renderer.lightNumDebug].color.X)
                    return;
                renderer.lights[renderer.lightNumDebug].color = new Vector4(value);
                NotifyValueChanged("LightStrength", value);
                renderer.uboLights.Update(renderer.lights);
            }
        }
        #endregion

        void updateChess () {
            //stockfish
            if (stockfishCmdQueue.Count > 0)
                askStockfishIsReady();

            switch (CurrentState) {
                case GameState.Init:
                    initBoard();
                    //initInterface();
                    initStockfish();
                    CurrentState = GameState.Play;
                    rebuildBuffers = true;
                    break;
                case GameState.Play:
                case GameState.Checked:
                    if (stockfish == null)
                        return;
                    if (string.IsNullOrEmpty(bestMove)) 
                        break;

                    if (!playerIsAi[(int)CurrentPlayer]) {
                        if (!AutoPlayHint) {
                            //createArrows(bestMove);
                            break;
                        }
                    }
                    //clearArrows();
                    processMove(bestMove);
                    bestMove = null;
                    break;
            }
			rebuildBuffers = true;
        }


        Vector4 validPosColor = new Vector4(0.5f, 0.5f, 0.9f, 0.7f);
        Vector4 activeColor = new Vector4(0.0f, 0.9f, 0.9f, 0.5f);
        Vector4 kingCheckedColor = new Vector4(1.0f, 0.1f, 0.1f, 0.8f);

        #region LOGS
        List<string> logBuffer = new List<string>();
        void AddLog(string msg) {
            if (string.IsNullOrEmpty(msg))
                return;
            logBuffer.Add(msg);
            NotifyValueChanged("LogBuffer", logBuffer);
            Console.WriteLine(msg);
        }
        #endregion

        #region Stockfish
        Process stockfish;
        volatile bool waitAnimationFinished;
        volatile bool waitStockfishIsReady;
        Queue<string> stockfishCmdQueue = new Queue<string>();
        List<String> stockfishMoves = new List<string>();

        public bool StockfishNotFound {
            get { return stockfish == null; }
        }
        public string StockfishPath {
            get { return Configuration.Global.Get<string>("StockfishPath"); }
            set {
                if (value == StockfishPath)
                    return;
                Configuration.Global.Set("StockfishPath", value);
                NotifyValueChanged("StockfishPath", value);

                initStockfish();
            }
        }
        public int StockfishLevel {
            get { return Configuration.Global.Get<int>("Level"); }
            set {
                if (value == StockfishLevel)
                    return;

                Configuration.Global.Set("Level", value);
                sendToStockfish("setoption name Skill Level value " + value.ToString());
                NotifyValueChanged("StockfishLevel", value);
            }
        }
        string stockfishPositionCommand {
            get {
                string tmp = "position startpos moves ";
                return
                    StockfishMoves.Count == 0 ? tmp : tmp + StockfishMoves.Aggregate((i, j) => i + " " + j);
            }
        }

        public bool AutoPlayHint {
            get { return Configuration.Global.Get<bool>("AutoPlayHint"); }
            set {
                if (value == AutoPlayHint)
                    return;
                Crow.Configuration.Global.Set("AutoPlayHint", value);
                NotifyValueChanged("AutoPlayHint", value);
            }
        }

        public List<String> StockfishMoves {
            get { return stockfishMoves; }
            set { stockfishMoves = value; }
        }

        void initStockfish() {
            if (stockfish != null) {
                resetBoard(false);

                stockfish.OutputDataReceived -= dataReceived;
                stockfish.ErrorDataReceived -= dataReceived;
                stockfish.Exited -= P_Exited;

                stockfish.Kill();
                stockfish = null;
            }

            if (!File.Exists(StockfishPath)) {
                NotifyValueChanged("StockfishNotFound", true);
                return;
            }

            stockfish = new Process();
            stockfish.StartInfo.UseShellExecute = false;
            stockfish.StartInfo.RedirectStandardOutput = true;
            stockfish.StartInfo.RedirectStandardInput = true;
            stockfish.StartInfo.RedirectStandardError = true;
            stockfish.EnableRaisingEvents = true;
            stockfish.StartInfo.FileName = StockfishPath;
            stockfish.OutputDataReceived += dataReceived;
            stockfish.ErrorDataReceived += dataReceived;
            stockfish.Exited += P_Exited;
            stockfish.Start();

            stockfish.BeginOutputReadLine();

            sendToStockfish("uci");
        }
        void syncStockfish() {
            NotifyValueChanged("StockfishMoves", StockfishMoves);
            sendToStockfish(stockfishPositionCommand);
        }
        void askStockfishIsReady() {
            if (waitStockfishIsReady)
                return;
            waitStockfishIsReady = true;
            stockfish.WaitForInputIdle();
            stockfish.StandardInput.WriteLine("isready");
        }
        void sendToStockfish(string msg) {
            stockfishCmdQueue.Enqueue(msg);
        }
        void P_Exited(object sender, EventArgs e) {
            AddLog("Stockfish Terminated");
        }
        void dataReceived(object sender, DataReceivedEventArgs e) {
            if (string.IsNullOrEmpty(e.Data))
                return;

            string[] tmp = e.Data.Split(' ');

            if (tmp[0] != "readyok")
                AddLog(e.Data);

            switch (tmp[0]) {
                case "readyok":
                    if (stockfishCmdQueue.Count == 0) {
                        AddLog("Error: no command on queue after readyok");
                        return;
                    }
                    string cmd = stockfishCmdQueue.Dequeue();
                    AddLog("=>" + cmd);
                    stockfish.WaitForInputIdle();
                    stockfish.StandardInput.WriteLine(cmd);
                    waitStockfishIsReady = false;
                    return;
                case "uciok":
                    NotifyValueChanged("StockfishNotFound", false);
                    sendToStockfish("setoption name Skill Level value " + StockfishLevel.ToString());
                    break;
                case "bestmove":
                    if (tmp[1] == "(none)")
                        return;
                    if (CurrentState == GameState.Checkmate) {
                        AddLog("Error: received bestmove while game in Checkmate state");
                        return;
                    }
                    bestMove = tmp[1];
                    break;
            }
        }

        #endregion

        #region game logic

        Piece[,] board;
        bool[] playerIsAi = { true, true };

        volatile GameState currentState = GameState.Init;
        Point selection;
        Point active = new Point(-1, -1);
        List<Point> ValidPositionsForActivePce = null;

        int cptWhiteOut = 0;
        int cptBlackOut = 0;

        volatile string bestMove;

        //public static ChessPlayer[] Players;

        Piece[] whites;
        Piece[] blacks;

        public GameState CurrentState {
            get { return currentState; }
            set {
                if (currentState == value)
                    return;
                currentState = value;
                NotifyValueChanged("CurrentState", currentState);
            }
        }

        public ChessColor CurrentPlayer;

        public ChessColor Opponent {
            get { return CurrentPlayer == ChessColor.White ? ChessColor.Black : ChessColor.White; }
        }
        public Piece[] OpponentPieces {
            get { return CurrentPlayer == ChessColor.White ? blacks : whites; }
        }
        public Piece[] CurrentPlayerPieces {
            get { return CurrentPlayer == ChessColor.White ? whites : blacks; }
        }
        public int GetPawnPromotionY (ChessColor color) => color == ChessColor.White ? 7 : 0;

        Point Active {
            get {
                return active;
            }
            set {
                active = value;
                if (active < 0)
                    NotifyValueChanged("ActCell", "");
                else
                    NotifyValueChanged("ActCell", getChessCell(active.X, active.Y));

                if (Active < 0) {
                    ValidPositionsForActivePce = null;
                    return;
                }

                ValidPositionsForActivePce = new List<Point>();

                foreach (string s in computeValidMove(Active)) {
                    bool kingIsSafe = true;

                    previewBoard(s);

                    kingIsSafe = checkKingIsSafe();

                    restoreBoardAfterPreview();

                    if (kingIsSafe)
                        addValidMove(getChessCell(s.Substring(2, 2)));
                }

                if (ValidPositionsForActivePce.Count == 0)
                    ValidPositionsForActivePce = null;
            }
        }
        Point Selection {
            get {
                return selection;
            }
            set {
				Piece.UpdateCase (selection.X, selection.Y, 1, 1, 1);
                selection = value;
                if (selection.X < 0)
                    selection.X = 0;
                else if (selection.X > 7)
                    selection.X = 7;
                if (selection.Y < 0)
                    selection.Y = 0;
                else if (selection.Y > 7)
                    selection.Y = 7;
				Piece.UpdateCase (selection.X, selection.Y, 1f, 1f,17f);
				NotifyValueChanged ("SelCell", getChessCell(selection.X, selection.Y));
            }
        }

        void initBoard() {
            CurrentPlayer = ChessColor.White;
            cptWhiteOut = 0;
            cptBlackOut = 0;
            StockfishMoves.Clear();
            NotifyValueChanged("StockfishMoves", StockfishMoves);

            Active = -1;

            board = new Piece[8, 8];
            instanceBuff = new HostBuffer<InstanceData>(dev, VkBufferUsageFlags.VertexBuffer, 97, true, false);
            Piece.instanceBuff = instanceBuff;

            List<Model.InstancedCmd> primitiveCmds = new List<Model.InstancedCmd>();
            primitiveCmds.Add(new Model.InstancedCmd { count = 16, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("pawn").Mesh) });
            for (int i = 0; i < 8; i++)
                addPiece(ChessColor.White, PieceType.Pawn, i, 1);
            for (int i = 0; i < 8; i++)
                addPiece(ChessColor.Black, PieceType.Pawn, i, 6);

            primitiveCmds.Add(new Model.InstancedCmd { count = 4, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("bishop").Mesh) });
            addPiece(ChessColor.White, PieceType.Bishop, 2, 0);
            addPiece(ChessColor.White, PieceType.Bishop, 5, 0);
            addPiece(ChessColor.Black, PieceType.Bishop, 2, 7);
            addPiece(ChessColor.Black, PieceType.Bishop, 5, 7);

            primitiveCmds.Add(new Model.InstancedCmd { count = 4, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("knight").Mesh) });
            addPiece(ChessColor.White, PieceType.Knight, 1, 0);
            addPiece(ChessColor.White, PieceType.Knight, 6, 0);
            addPiece(ChessColor.Black, PieceType.Knight, 1, 7);
            addPiece(ChessColor.Black, PieceType.Knight, 6, 7);

            primitiveCmds.Add(new Model.InstancedCmd { count = 4, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("rook").Mesh) });
            addPiece(ChessColor.White, PieceType.Rook, 0, 0);
            addPiece(ChessColor.White, PieceType.Rook, 7, 0);
            addPiece(ChessColor.Black, PieceType.Rook, 0, 7);
            addPiece(ChessColor.Black, PieceType.Rook, 7, 7);

            primitiveCmds.Add(new Model.InstancedCmd { count = 2, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("queen").Mesh) });
            addPiece(ChessColor.White, PieceType.Queen, 3, 0);
            addPiece(ChessColor.Black, PieceType.Queen, 3, 7);

            primitiveCmds.Add(new Model.InstancedCmd { count = 2, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("king").Mesh) });
            addPiece(ChessColor.White, PieceType.King, 4, 0);
            addPiece(ChessColor.Black, PieceType.King, 4, 7);

            whites = board.Cast<Piece>().Where(p => p?.Player == ChessColor.White).ToArray();
            blacks = board.Cast<Piece>().Where(p => p?.Player == ChessColor.Black).ToArray();

            uint curInstIdx = 32;
            for (int x = 0; x < 8; x++) {
                for (int y = 0; y < 8; y++) {
                    string name = string.Format($"{(char)(y+97)}{(x+1).ToString()}");
                    primitiveCmds.Add(new Model.InstancedCmd { count = 1, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode(name).Mesh) });
                    instanceBuff.Update(curInstIdx, new InstanceData(new Vector4(1), Matrix4x4.Identity));
                    curInstIdx++;
                }
            }
            primitiveCmds.Add(new Model.InstancedCmd { count = 1, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("frame").Mesh) });
            instanceBuff.Update (96, new InstanceData(new Vector4(1), Matrix4x4.Identity));
            Piece.flushEnd = 97;

			Piece.UpdateCase (0, 0, 17f, 0f, 0f);

			instancedCmds = primitiveCmds.ToArray();
        }
        void resetBoard(bool animate = true) {
            CurrentState = GameState.Play;

            CurrentPlayer = ChessColor.White;
            cptWhiteOut = 0;
            cptBlackOut = 0;
            StockfishMoves.Clear();
            NotifyValueChanged("StockfishMoves", StockfishMoves);

            Active = -1;
            board = new Piece[8, 8];

            foreach (Piece p in whites) {
                p.Reset(animate);
                board[p.initX, p.initY] = p;
            }
            foreach (Piece p in blacks) {
                p.Reset(animate);
                board[p.initX, p.initY] = p;
            }

        }
        void addPiece(ChessColor player, PieceType _type, int col, int line) {
            Piece p = new Piece(player, _type, col, line);
            board[col, line] = p;
        }

        void addValidMove(Point p) {
            if (ValidPositionsForActivePce.Contains(p))
                return;
            ValidPositionsForActivePce.Add(p);
        }

        bool checkKingIsSafe() {
            foreach (Piece op in OpponentPieces) {
                if (op.Captured)
                    continue;
                foreach (string opM in computeValidMove(op.BoardCell)) {
                    if (opM.EndsWith("K"))
                        return false;
                }
            }
            return true;
        }
        string[] getLegalMoves() {

            List<String> legalMoves = new List<string>();

            foreach (Piece p in CurrentPlayerPieces) {
                if (p.Captured)
                    continue;
                foreach (string s in computeValidMove(p.BoardCell)) {
                    bool kingIsSafe = true;

                    previewBoard(s);

                    kingIsSafe = checkKingIsSafe();

                    restoreBoardAfterPreview();

                    if (kingIsSafe)
                        legalMoves.Add(s);
                }
            }
            return legalMoves.ToArray();
        }
        string[] checkSingleMove(Point pos, int xDelta, int yDelta) {
            int x = pos.X + xDelta;
            int y = pos.Y + yDelta;

            if (x < 0 || x > 7 || y < 0 || y > 7)
                return null;

            if (board[x, y] == null) {
                if (board[pos.X, pos.Y].Type == PieceType.Pawn) {
                    if (xDelta != 0) {
                        //check En passant capturing
                        int epY;
                        string validEP;
                        if (board[pos.X, pos.Y].Player == ChessColor.White) {
                            epY = 4;
                            validEP = getChessCell(x, 6) + getChessCell(x, 4);
                        } else {
                            epY = 3;
                            validEP = getChessCell(x, 1) + getChessCell(x, 3);
                        }
                        if (pos.Y != epY)
                            return null;
                        if (board[x, epY] == null)
                            return null;
                        if (board[x, epY].Type != PieceType.Pawn)
                            return null;
                        if (StockfishMoves[StockfishMoves.Count - 1] != validEP)
                            return null;
                        return new string[] { getChessCell(pos.X, pos.Y) + getChessCell(x, y) + "EP" };
                    }
                    //check pawn promotion
                    if (y == GetPawnPromotionY (board[pos.X, pos.Y].Player)) {
                        string basicPawnMove = getChessCell(pos.X, pos.Y) + getChessCell(x, y);
                        return new string[] {
                            basicPawnMove + "q",
                            basicPawnMove + "k",
                            basicPawnMove + "r",
                            basicPawnMove + "b"
                        };
                    }
                }
                return new string[] { getChessCell(pos.X, pos.Y) + getChessCell(x, y) };
            }

            if (board[x, y].Player == board[pos.X, pos.Y].Player)
                return null;
            if (board[pos.X, pos.Y].Type == PieceType.Pawn && xDelta == 0)
                return null;//pawn cant take in front

            if (board[x, y].Type == PieceType.King)
                return new string[] { getChessCell(pos.X, pos.Y) + getChessCell(x, y) + "K" };

            if (board[pos.X, pos.Y].Type == PieceType.Pawn &&
                y == GetPawnPromotionY(board[pos.X, pos.Y].Player)) {
                string basicPawnMove = getChessCell(pos.X, pos.Y) + getChessCell(x, y);
                return new string[] {
                    basicPawnMove + "q",
                    basicPawnMove + "k",
                    basicPawnMove + "r",
                    basicPawnMove + "b"
                };
            }

            return new string[] { getChessCell(pos.X, pos.Y) + getChessCell(x, y) };
        }
        string[] checkIncrementalMove(Point pos, int xDelta, int yDelta) {

            List<string> legalMoves = new List<string>();

            int x = pos.X + xDelta;
            int y = pos.Y + yDelta;

            string strStart = getChessCell(pos.X, pos.Y);

            while (x >= 0 && x < 8 && y >= 0 && y < 8) {
                if (board[x, y] == null) {
                    legalMoves.Add(strStart + getChessCell(x, y));
                    x += xDelta;
                    y += yDelta;
                    continue;
                }

                if (board[x, y].Player == board[pos.X, pos.Y].Player)
                    break;

                if (board[x, y].Type == PieceType.King)
                    legalMoves.Add(strStart + getChessCell(x, y) + "K");
                else
                    legalMoves.Add(strStart + getChessCell(x, y));

                break;
            }
            return legalMoves.ToArray();
        }
        string[] computeValidMove(Point pos) {
            int x = pos.X;
            int y = pos.Y;

            Piece p = board[x, y];

            ChessMoves validMoves = new ChessMoves();

            if (p != null) {
                switch (p.Type) {
                    case PieceType.Pawn:
                        int pawnDirection = 1;
                        if (p.Player == ChessColor.Black)
                            pawnDirection = -1;
                        validMoves.AddMove(checkSingleMove(pos, 0, 1 * pawnDirection));
                        if (board[x, y + pawnDirection] == null && !p.HasMoved)
                            validMoves.AddMove(checkSingleMove(pos, 0, 2 * pawnDirection));
                        validMoves.AddMove(checkSingleMove(pos, -1, 1 * pawnDirection));
                        validMoves.AddMove(checkSingleMove(pos, 1, 1 * pawnDirection));
                        break;
                    case PieceType.Rook:
                        validMoves.AddMove(checkIncrementalMove(pos, 0, 1));
                        validMoves.AddMove(checkIncrementalMove(pos, 0, -1));
                        validMoves.AddMove(checkIncrementalMove(pos, 1, 0));
                        validMoves.AddMove(checkIncrementalMove(pos, -1, 0));
                        break;
                    case PieceType.Knight:
                        validMoves.AddMove(checkSingleMove(pos, 2, 1));
                        validMoves.AddMove(checkSingleMove(pos, 2, -1));
                        validMoves.AddMove(checkSingleMove(pos, -2, 1));
                        validMoves.AddMove(checkSingleMove(pos, -2, -1));
                        validMoves.AddMove(checkSingleMove(pos, 1, 2));
                        validMoves.AddMove(checkSingleMove(pos, -1, 2));
                        validMoves.AddMove(checkSingleMove(pos, 1, -2));
                        validMoves.AddMove(checkSingleMove(pos, -1, -2));
                        break;
                    case PieceType.Bishop:
                        validMoves.AddMove(checkIncrementalMove(pos, 1, 1));
                        validMoves.AddMove(checkIncrementalMove(pos, -1, -1));
                        validMoves.AddMove(checkIncrementalMove(pos, 1, -1));
                        validMoves.AddMove(checkIncrementalMove(pos, -1, 1));
                        break;
                    case PieceType.King:
                        if (!p.HasMoved) {
                            Piece tower = board[0, y];
                            if (tower != null) {
                                if (!tower.HasMoved) {
                                    for (int i = 1; i < x; i++) {
                                        if (board[i, y] != null)
                                            break;
                                        if (i == x - 1)
                                            validMoves.Add(getChessCell(x, y) + getChessCell(x - 2, y));
                                    }
                                }
                            }
                            tower = board[7, y];
                            if (tower != null) {
                                if (!tower.HasMoved) {
                                    for (int i = x + 1; i < 7; i++) {
                                        if (board[i, y] != null)
                                            break;
                                        if (i == 6)
                                            validMoves.Add(getChessCell(x, y) + getChessCell(x + 2, y));
                                    }
                                }
                            }
                        }

                        validMoves.AddMove(checkSingleMove(pos, -1, -1));
                        validMoves.AddMove(checkSingleMove(pos, -1, 0));
                        validMoves.AddMove(checkSingleMove(pos, -1, 1));
                        validMoves.AddMove(checkSingleMove(pos, 0, -1));
                        validMoves.AddMove(checkSingleMove(pos, 0, 1));
                        validMoves.AddMove(checkSingleMove(pos, 1, -1));
                        validMoves.AddMove(checkSingleMove(pos, 1, 0));
                        validMoves.AddMove(checkSingleMove(pos, 1, 1));

                        break;
                    case PieceType.Queen:
                        validMoves.AddMove(checkIncrementalMove(pos, 0, 1));
                        validMoves.AddMove(checkIncrementalMove(pos, 0, -1));
                        validMoves.AddMove(checkIncrementalMove(pos, 1, 0));
                        validMoves.AddMove(checkIncrementalMove(pos, -1, 0));
                        validMoves.AddMove(checkIncrementalMove(pos, 1, 1));
                        validMoves.AddMove(checkIncrementalMove(pos, -1, -1));
                        validMoves.AddMove(checkIncrementalMove(pos, 1, -1));
                        validMoves.AddMove(checkIncrementalMove(pos, -1, 1));
                        break;
                }
            }
            return validMoves.ToArray();
        }

        string preview_Move;
        bool preview_MoveState;
        bool preview_wasPromoted;
        Piece preview_Captured;

        void previewBoard(string move) {
            if (move.EndsWith("K")) {
                AddLog("Previewing: " + move);
                move = move.Substring(0, 4);
            }

            preview_Move = move;

            Point pStart = getChessCell(preview_Move.Substring(0, 2));
            Point pEnd = getChessCell(preview_Move.Substring(2, 2));
            Piece p = board[pStart.X, pStart.Y];

            //pawn promotion
            if (preview_Move.Length == 5) {
                p.Promote(preview_Move[4], true);
                preview_wasPromoted = true;
            } else
                preview_wasPromoted = false;

            preview_MoveState = p.HasMoved;
            board[pStart.X, pStart.Y] = null;
            p.HasMoved = true;

            //pawn en passant
            if (preview_Move.Length == 6)
                preview_Captured = board[pEnd.X, pStart.Y];
            else
                preview_Captured = board[pEnd.X, pEnd.Y];

            if (preview_Captured != null)
                preview_Captured.Captured = true;

            board[pEnd.X, pEnd.Y] = p;
        }
        void restoreBoardAfterPreview() {
            Point pStart = getChessCell(preview_Move.Substring(0, 2));
            Point pEnd = getChessCell(preview_Move.Substring(2, 2));
            Piece p = board[pEnd.X, pEnd.Y];
            p.HasMoved = preview_MoveState;
            if (preview_wasPromoted)
                p.Unpromote();
            if (preview_Captured != null)
                preview_Captured.Captured = false;
            board[pStart.X, pStart.Y] = p;
            board[pEnd.X, pEnd.Y] = null;
            if (preview_Move.Length == 6)
                board[pEnd.X, pStart.Y] = preview_Captured;
            else
                board[pEnd.X, pEnd.Y] = preview_Captured;
            preview_Move = null;
            preview_Captured = null;
        }

        string getChessCell(int col, int line) {
            char c = (char)(col + 97);
            return c.ToString() + (line + 1).ToString();
        }
        Point getChessCell(string s) {
            return new Point((int)s[0] - 97, int.Parse(s[1].ToString()) - 1);
        }
        Vector3 getCurrentCapturePosition(Piece p) {
            float x, y;
            if (p.Player == ChessColor.White) {
                x = -1.0f;
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
            return new Vector3(x, y, -0.25f);
        }

        void capturePiece(Piece p, bool animate = true) {
            Point pos = p.BoardCell;
            board[pos.X, pos.Y] = null;

            Vector3 capturePos = getCurrentCapturePosition(p);

            if (p.Player == ChessColor.White)
                cptWhiteOut++;
            else
                cptBlackOut++;

            p.Captured = true;
            p.HasMoved = true;

            if (animate)
                Animation.StartAnimation(new PathAnimation(p, "Position",
                    new BezierPath(
                        p.Position,
                        capturePos, Vector3.UnitZ)));
            else
                p.Position = capturePos;
        }

        void processMove(string move, bool animate = true) {
            if (waitAnimationFinished)
                return;
            if (string.IsNullOrEmpty(move))
                return;
            if (move == "(none)")
                return;

            Point pStart = getChessCell(move.Substring(0, 2));
            Point pEnd = getChessCell(move.Substring(2, 2));

            Piece p = board[pStart.X, pStart.Y];
            if (p == null) {
                AddLog("ERROR: impossible move.");
                return;
            }
            bool enPassant = false;
            if (p.Type == PieceType.Pawn && pStart.X != pEnd.X && board[pEnd.X, pEnd.Y] == null)
                enPassant = true;

            StockfishMoves.Add(move);
            NotifyValueChanged("StockfishMoves", StockfishMoves);

            board[pStart.X, pStart.Y] = null;
            Point pTarget = pEnd;
            if (enPassant)
                pTarget.Y = pStart.Y;
            if (board[pTarget.X, pTarget.Y] != null)
                capturePiece(board[pTarget.X, pTarget.Y], animate);
            board[pEnd.X, pEnd.Y] = p;
            p.HasMoved = true;

            Vector3 targetPosition = new Vector3(pEnd.X, pEnd.Y, 0f);
            if (animate) {
                Animation.StartAnimation(new PathAnimation(p, "Position",
                    new BezierPath(
                        p.Position,
                        targetPosition, Vector3.UnitZ),50),
                    0, move_AnimationFinished);
                waitAnimationFinished = true;
            } else
                p.Position = targetPosition;

            Active = -1;

            if (!enPassant) {
                //check if rockMove
                if (p.Type == PieceType.King) {
                    int xDelta = pStart.X - pEnd.X;
                    if (Math.Abs(xDelta) == 2) {
                        //rocking
                        if (xDelta > 0) {
                            pStart.X = 0;
                            pEnd.X = pEnd.X + 1;
                        } else {
                            pStart.X = 7;
                            pEnd.X = pEnd.X - 1;
                        }
                        p = board[pStart.X, pStart.Y];
                        board[pStart.X, pStart.Y] = null;
                        board[pEnd.X, pEnd.Y] = p;
                        p.HasMoved = true;

                        targetPosition = new Vector3(pEnd.X, pEnd.Y, 0f);
                        if (animate)
                            Animation.StartAnimation(new PathAnimation(p, "Position",
                                new BezierPath(
                                    p.Position,
                                    targetPosition, Vector3.UnitZ * 2f)));
                        else
                            p.Position = targetPosition;
                    }
                }

                //check promotion
                if (move.Length == 5)
                    p.Promote(move[4]);
            }
            NotifyValueChanged("board", board);
        }

        void undoLastMove() {
            if (StockfishMoves.Count == 0)
                return;

            string move = StockfishMoves[StockfishMoves.Count - 1];
            StockfishMoves.RemoveAt(StockfishMoves.Count - 1);

            Point pPreviousPos = getChessCell(move.Substring(0, 2));
            Point pCurPos = getChessCell(move.Substring(2, 2));

            Piece p = board[pCurPos.X, pCurPos.Y];

            replaySilently();

            p.Position = new Vector3(pCurPos.X, pCurPos.Y, 0f);

            Animation.StartAnimation(new PathAnimation(p, "Position",
                new BezierPath(
                    p.Position,
                    new Vector3(pPreviousPos.X, pPreviousPos.Y, 0f), Vector3.UnitZ)));

            syncStockfish();

            //animate undo capture
            Piece pCaptured = board[pCurPos.X, pCurPos.Y];
            if (pCaptured == null)
                return;
            Vector3 pCapLastPos = pCaptured.Position;
            pCaptured.Position = getCurrentCapturePosition(pCaptured);

            Animation.StartAnimation(new PathAnimation(pCaptured, "Position",
                new BezierPath(
                    pCaptured.Position,
                    pCapLastPos, Vector3.UnitZ)));

        }
        void replaySilently() {
            string[] moves = StockfishMoves.ToArray();
            resetBoard(false);
            foreach (string m in moves) {
                processMove(m, false);
                CurrentPlayer = Opponent;
            }
            //CurrentPlayerIndex = currentPlayerIndex;
        }

        void switchPlayer() {
            bestMove = null;

            CurrentPlayer = Opponent;

            syncStockfish();

            if (playerIsAi [(int)CurrentPlayer])
                sendToStockfish("go");
        }

        void move_AnimationFinished(Animation a) {
            waitAnimationFinished = false;

            switchPlayer();

            bool kingIsSafe = checkKingIsSafe();
            if (getLegalMoves().Length == 0) {
                if (kingIsSafe)
                    CurrentState = GameState.Pad;
                else {
                    CurrentState = GameState.Checkmate;
                    //GraphicObject g = Load("#Chess.gui.checkmate.crow");
                    //g.DataSource = this;
                    Piece king = CurrentPlayerPieces.First(p => p.Type == PieceType.King);
                    Animation.StartAnimation(new FloatAnimation(king, "Z", 0.4f, 0.04f));
                    Animation.StartAnimation(new AngleAnimation(king, "XAngle", MathHelper.Pi * 0.55f, 0.09f));
                    Animation.StartAnimation(new AngleAnimation(king, "ZAngle", king.ZAngle  + 0.3f, 0.5f));
                }
            } else if (kingIsSafe)
                CurrentState = GameState.Play;
            else
                CurrentState = GameState.Checked;
        }

        #endregion    
    }
}