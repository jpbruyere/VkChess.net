// Copyright (c) 2016-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.ComponentModel;
using Crow;
using Crow.Drawing;
//using vkvg;

namespace vkChess
{
	public class BoardWidget : Widget
	{
		Picture sprites;
		Piece[,] board;

		[DefaultValue(null)]
		public virtual Piece[,] Board {
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
		public BoardWidget ():base()
		{
			sprites = new SvgPicture ("data/Pieces.svg");
		}

		protected override void onDraw (Context gr)
		{
			base.onDraw (gr);

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

			if (board == null)
				return;
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
		}
	}
}

