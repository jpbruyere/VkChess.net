// Copyright (c) 2020  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Collections;
using Crow.Drawing;
using Crow;
using System.Collections.Generic;

namespace vkChess
{
	public class MoveHistoryWidget : ScrollingObject
	{
		IList<string> moves;
		int visibleLines = 1;
		FontExtents fe;

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

					updateMaxScrollAndFocusedIndices ();
				} else
					ScrollY = MaxScrollY = CurrentMoveIndex = hoverMoveIdx = 0;
				NotifyValueChanged ("Moves", moves);
				RegisterForGraphicUpdate ();
			}
		}
		void updateMaxScrollAndFocusedIndices () {
			MaxScrollY = moves.Count - visibleLines;
			ScrollY = 0;
			CurrentMoveIndex = 0;
			hoverMoveIdx = - 1;
		}
		void Lines_ListAdd (object sender, ListChangedEventArg e)
		{
			updateMaxScrollAndFocusedIndices ();
			RegisterForRedraw ();
		}

		void Lines_ListRemove (object sender, ListChangedEventArg e)
		{
			updateMaxScrollAndFocusedIndices ();
			RegisterForRedraw ();
		}
		void Lines_ListClear (object sender, ListClearEventArg e)
		{
			ScrollY = MaxScrollY = CurrentMoveIndex = 0;
			hoverMoveIdx = -1;
			RegisterForRedraw ();
		}



		public override void OnLayoutChanges (LayoutingType layoutType)
		{
			base.OnLayoutChanges (layoutType);

			if (layoutType == LayoutingType.Height) {
				using (ImageSurface img = new ImageSurface (Format.Argb32, 10, 10)) {
					using (Context gr = new Context (img)) {
						//Cairo.FontFace cf = gr.GetContextFontFace ();
						this.setFontForContext (gr);
						fe = gr.FontExtents;
					}
				}
				lineHeight = fe.Height + 2 * moveMargin + moveSpacing;
				visibleLines = (int)Math.Floor ((double)ClientRectangle.Height / lineHeight);
				MaxScrollY = moves == null ? 0 : moves.Count - visibleLines;
			}
		}
		double lineHeight = 1;

		int moveMargin = 3, moveSpacing = 2;

		int hoverMoveIdx = -1, currentMoveIndex;

		public int CurrentMoveIndex {
			get => currentMoveIndex;
			set {
				if (currentMoveIndex == value)
					return;
				currentMoveIndex = value;
				NotifyValueChangedAuto (currentMoveIndex);
				RegisterForRedraw ();
			}
		}

		protected override void onDraw (Context gr)
		{
			base.onDraw (gr);

			if (moves == null)
				return;

			gr.SelectFontFace (Font.Name, Font.Slant, Font.Wheight);
			gr.SetFontSize (Font.Size);

			Rectangle r = ClientRectangle;


			double y = ClientRectangle.Y;
			double x = ClientRectangle.X - ScrollX;

			string[] movesCopy;
			lock (moves) {
				movesCopy = new string[moves.Count];
				moves.CopyTo (movesCopy, 0);
			}


			for (int i = 0; i < visibleLines; i++) {
				int idx = i + ScrollY;
				int mi = movesCopy.Length - (1 + idx);
				if (mi < 0)
					break;
				string m = movesCopy[mi];
				Color bg;
				Color fg;
				if (mi % 2 > 0) {
					if (idx == currentMoveIndex) {
						bg = Colors.DarkBlue;
						fg = Colors.White;
					} else if (idx == hoverMoveIdx) {
						bg = Colors.Black;
						fg = Colors.White;
					} else {
						bg = Colors.Black;
						fg = Colors.DimGrey;
					}
				} else {
					if (idx == currentMoveIndex) {
						bg = Colors.Blue;
						fg = Colors.White;
					} else if (idx == hoverMoveIdx) {
						bg = Colors.White;
						fg = Colors.Black;
					} else {
						bg = Colors.Grey;
						fg = Colors.DarkGrey;
					}
				}

				gr.SetSource (bg);
				gr.Rectangle (x, y, (double)r.Width, lineHeight - moveSpacing);
				gr.Fill ();

				y += moveMargin;

				gr.SetSource (fg);
				TextExtents te = gr.TextExtents(m);
				gr.MoveTo (x + 0.5 * (r.Width - te.Width), y + fe.Ascent);
				gr.ShowText (m);
				y += fe.Height + moveMargin + moveSpacing;
			}
		}
		public override void onMouseMove(object sender, MouseMoveEventArgs e)
		{
			base.onMouseMove(sender, e);

			if (Moves == null) {
				hoverMoveIdx = -1;
				return;
			}

			PointD mouseLocalPos = ScreenPointToLocal (e.Position);

			hoverMoveIdx = ScrollY + (int)Math.Min (Math.Max (0, Math.Floor (mouseLocalPos.Y / lineHeight)), moves.Count - 1);
			RegisterForRedraw ();
		}
		public override void onMouseClick(object sender, MouseButtonEventArgs e)
		{
			if  (e.Button == Glfw.MouseButton.Left) {
				CurrentMoveIndex = hoverMoveIdx;
				e.Handled = true;
			}
			base.onMouseClick(sender, e);
		}
		public override void onMouseLeave(object sender, MouseMoveEventArgs e)
		{
			hoverMoveIdx = -1;
			base.onMouseLeave(sender, e);
		}
	}
}

