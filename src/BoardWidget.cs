// Copyright (c) 2016-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Crow;
using Crow.Drawing;
//using vkvg;

namespace vkChess
{
	public class BoardWidget : Widget
	{
		Picture sprites;
		string filePath;
		IList<string> moves;
		string[,] currentBoard;
		Piece[,] board;

		[DefaultValue(null)]
		public Piece[,] Board {
			get { return board; }
			set {
				if (board == value) {
					RegisterForGraphicUpdate ();
					return;
				}
				board = value;
				NotifyValueChanged ("Board", board);
				RegisterForGraphicUpdate ();
			}
		}
		public virtual IList<string> Moves {
			get => moves;
			set {
				if (moves == value)
					return;

				if (moves is ObservableList<string> ol) {
					ol.ListAdd -= Lines_ListAdd;
					ol.ListRemove -= Lines_ListRemove;
					ol.ListClear -= Lines_ListClear;
				}

				moves = value;

				if (moves is ObservableList<string> olNew) {
					olNew.ListAdd += Lines_ListAdd;
					olNew.ListRemove += Lines_ListRemove;
					olNew.ListClear += Lines_ListClear;
				}

				NotifyValueChanged ("Moves", moves);

				replayAllMoves ();

				RegisterForGraphicUpdate ();
			}
		}
		public string FilePath {
			get => FilePath;
			set {
				if (filePath == value)
					return;
				filePath = value;
				NotifyValueChangedAuto (filePath);
				RegisterForGraphicUpdate ();

				if (string.IsNullOrEmpty (filePath))
					resetCurrentBoard ();
				else
					loadFromFile ();
			}
		}
		public BoardWidget (): base()
		{
			sprites = new SvgPicture ("data/Pieces.svg");
		}
		static readonly string[,] boardInit = {
			{"wr", "wp",   "",  "",  "",  "", "bp", "br"},
			{"wk", "wp",   "",  "",  "",  "", "bp", "bk"},
			{"wb", "wp",   "",  "",  "",  "", "bp", "bb"},
			{"wq", "wp",   "",  "",  "",  "", "bp", "bq"},
			{"wK", "wp",   "",  "",  "",  "", "bp", "bK"},
			{"wb", "wp",   "",  "",  "",  "", "bp", "bb"},
			{"wk", "wp",   "",  "",  "",  "", "bp", "bk"},
			{"wr", "wp",   "",  "",  "",  "", "bp", "br"}
		};
		void Lines_ListAdd (object sender, ListChangedEventArg e)
		{
			processMove (currentBoard, e.Element.ToString());
			RegisterForRedraw ();
		}

		void Lines_ListRemove (object sender, ListChangedEventArg e)
		{
			replayAllMoves ();
			RegisterForRedraw ();
		}
		void Lines_ListClear (object sender, ListClearEventArg e)
		{
			resetCurrentBoard();
			RegisterForRedraw ();
		}
		protected override void onDraw (Context gr)
		{
			base.onDraw (gr);

			this.setFontForContext (gr);

			/*gr.FontFace = Font.Name;
			gr.FontSize = (uint)Font.Size;*/

			FontExtents fe = gr.FontExtents;

			Rectangle r = ClientRectangle;
			r.Inflate (-(int)fe.Height);
			double gridSize;
			double gridX = r.X;
			double gridY = r.Y;

			if (r.Width > r.Height) {
				gridX += (r.Width - r.Height) / 2;
				gridSize = r.Height;
			} else {
				gridY += (r.Height - r.Width) / 2;
				gridSize = r.Width;
			}

			Fill white = new SolidColor (Colors.Ivory);
			white.SetAsSource (IFace, gr);
			gr.Rectangle (gridX, gridY, gridSize, gridSize);
			gr.Fill ();

			double cellSize = gridSize / 8;

			Fill black = new SolidColor (Colors.Grey);
			black.SetAsSource (IFace, gr);
			for (int x = 0; x < 8; x++) {
				for (int y = 0; y < 8; y++) {
					if ((x + y) % 2 != 0)
						gr.Rectangle (gridX + x * cellSize, gridY + y * cellSize, cellSize, cellSize);
				}
			}
			gr.Fill ();

			Foreground.SetAsSource (IFace, gr);
			for (int x = 0; x < 8; x++) {
				string L = new string(new char[] { (char)(x + 97)});
				gr.MoveTo (gridX + x * cellSize - gr.TextExtents(L).XAdvance / 2 + cellSize / 2,
					gridY + gridSize + fe.Ascent + 1);
				gr.ShowText (L);
			}
			for (int y = 0; y < 8; y++) {
				string L = (y + 1).ToString();
				gr.MoveTo (gridX - gr.TextExtents(L).XAdvance - 1,
					gridY + fe.Ascent + cellSize * (7-y) + cellSize / 2 - fe.Height / 2);
				gr.ShowText (L);
			}
			gr.Fill ();

			if (board != null) {
				for (int x = 0; x < 8; x++) {
					for (int y = 0; y < 8; y++) {
						if (board [x, y] == null)
							continue;

						string spriteName;
						if (board [x, y].Player == ChessColor.White)
							spriteName = "w";
						else
							spriteName = "b";

						switch (board[x,y].Type) {
						case PieceType.Pawn:
							spriteName += "p";
							break;
						case PieceType.Rook:
							spriteName += "r";
							break;
						case PieceType.Knight:
							spriteName += "k";
							break;
						case PieceType.Bishop:
							spriteName += "b";
							break;
						case PieceType.King:
							spriteName += "K";
							break;
						case PieceType.Queen:
							spriteName += "q";
							break;
						}
						sprites.Paint (IFace, gr,new Rectangle(
							(int)(gridX + x * cellSize),
							(int)(gridY + (7 - y) * cellSize),
							(int)cellSize,
							(int)cellSize),spriteName);
					}
				}
			} else if (moves != null && currentBoard != null) {
				for (int x = 0; x < 8; x++) {
					for (int y = 0; y < 8; y++) {
						if (string.IsNullOrEmpty(currentBoard [x, y]))
							continue;
						sprites.Paint (IFace, gr,new Rectangle(
							(int)(gridX + x * cellSize),
							(int)(gridY + (7 - y) * cellSize),
							(int)cellSize,
							(int)cellSize),currentBoard[x, y]);
					}
				}
			}
		}
		void loadFromFile () {
			try {
				using (Stream stream = new FileStream (filePath, FileMode.Open))
					using (StreamReader sw = new StreamReader (stream))
						Moves = sw.ReadLine ().Split (' ');
			} catch (Exception ex) {
				Crow.MessageBox.ShowModal (IFace, MessageBox.Type.Error, $"Failed loading {filePath}\n{ex.Message}");
			}
		}
		void resetCurrentBoard () => currentBoard = boardInit.Clone() as string[,];
		void replayAllMoves () {
			resetCurrentBoard ();
			if (moves == null)
				return;
			int i = 0;

			lock (VkChess.movesMutex) {
				foreach (string move in moves) {
					try {
						processMove (currentBoard, move);
					} catch {
						Console.WriteLine ($"move: {move} i:{i}");
					}
					i++;
				}
			}
		}
		void processMove (string[,] board, string move) {
			ReadOnlySpan<char> m = move.AsSpan();
			Point p0 = VkChess.getChessCell (m.Slice (0, 2));
			Point p1 = VkChess.getChessCell (m.Slice (2, 2));

			if (board[p0.X, p0.Y][1] == 'p') {//pawn
				if (m.Length == 5) //pawnPromotion
					board[p0.X, p0.Y] = board[p0.X, p0.Y][0].ToString() + m[4];
				else if (p0.X != p1.X && string.IsNullOrEmpty (board[p1.X, p1.Y]))//prise en passant
					board[p0.X, p1.Y] = null;
			}else if (board[p0.X, p0.Y][1] == 'K' && Math.Abs(p0.X - p1.X) > 1) {//rook
				if (p0.X - p1.X < 0) {//small rock
					board[5, p0.Y] = board[7, p0.Y];
					board[7, p0.Y] = null;
				} else { //big rock
					board[3, p0.Y] = board[0, p0.Y];
					board[0, p0.Y] = null;
				}
			}
			board[p1.X, p1.Y] = board[p0.X, p0.Y];
			board[p0.X, p0.Y] = null;
		}
	}
}

