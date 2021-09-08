// Copyright (c) 2020  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Collections;
using Crow.Drawing;

namespace Crow
{
	public enum LogLevel
	{
		Minimal, Normal, Full, Debug
	}
	public enum LogType {
		Low,
		Normal,
		High,
		Debug,
		Warning,
		Error,
		Custom1,
		Custom2,
		Custom3,
	}
	public class LogEntry {
		public LogType Type;
		public string msg;
		public LogEntry (LogType type, string message) {
			Type = type;
			msg = message;
		}
		public override string ToString() => msg;
	}
	public class LogViewerWidget : ScrollingObject
	{
		ObservableList<LogEntry> lines;
		bool scrollOnOutput;
		int visibleLines = 1;
		FontExtents fe;

		[DefaultValue(true)]
		public virtual bool ScrollOnOutput {
			get { return scrollOnOutput; }
			set {
				if (scrollOnOutput == value)
					return;
				scrollOnOutput = value;
				NotifyValueChanged ("ScrollOnOutput", scrollOnOutput);

			}
		}
		public virtual ObservableList<LogEntry> Lines {
			get { return lines; }
			set {
				if (lines == value)
					return;
				if (lines != null) {
					lines.ListAdd -= Lines_ListAdd;
					lines.ListRemove -= Lines_ListRemove;
					lines.ListClear -= Lines_ListClear;
				}
				lines = value;
				if (lines != null) {
					lines.ListAdd += Lines_ListAdd;
					lines.ListRemove += Lines_ListRemove;
					lines.ListClear += Lines_ListClear;

					MaxScrollY = lines.Count - visibleLines;
					if (scrollOnOutput)
						ScrollY = MaxScrollY;
				}
				NotifyValueChanged ("Lines", lines);
				RegisterForGraphicUpdate ();
			}
		}
		void Lines_ListClear (object sender, EventArgs e)
		{
			MaxScrollY = MaxScrollX = 0;
		}

		void Lines_ListAdd (object sender, ListChangedEventArg e)
		{
			// try
			// {
				MaxScrollY = lines.Count - visibleLines;
				if (scrollOnOutput)
					ScrollY = MaxScrollY;
				
			// }
			// catch (System.Exception ex)
			// {
			// 	Console.WriteLine ($"list add valueChange handler bug:{ex}");
			// }
		}

		void Lines_ListRemove (object sender, ListChangedEventArg e)
		{
			MaxScrollY = lines.Count - visibleLines;
		}

		public override void OnLayoutChanges (LayoutingType layoutType)
		{
			base.OnLayoutChanges (layoutType);

			if (layoutType == LayoutingType.Height) {
				using (ImageSurface img = new ImageSurface (Format.Argb32, 10, 10)) {
					using (Context gr = new Context (img)) {
						//Cairo.FontFace cf = gr.GetContextFontFace ();

						gr.SelectFontFace (Font.Name, Font.Slant, Font.Wheight);
						gr.SetFontSize (Font.Size);

						fe = gr.FontExtents;
					}
				}
				visibleLines = (int)Math.Floor ((double)ClientRectangle.Height / fe.Height);
				MaxScrollY = lines == null ? 0 : lines.Count - visibleLines;
			}
		}
		protected override void onDraw (Context gr)
		{
			base.onDraw (gr);

			if (lines == null)
				return;

			gr.SelectFontFace (Font.Name, Font.Slant, Font.Wheight);
			gr.SetFontSize (Font.Size);

			Rectangle r = ClientRectangle;


			double y = ClientRectangle.Y;
			double x = ClientRectangle.X - ScrollX;

			lock (lines) {
				for (int i = 0; i < visibleLines; i++) {
					if (i + ScrollY >= Lines.Count)
						break;
					//if ((lines [i + Scroll] as string).StartsWith ("error", StringComparison.OrdinalIgnoreCase)) {
					//	errorFill.SetAsSource (gr);
					//	gr.Rectangle (x, y, (double)r.Width, fe.Height);
					//	gr.Fill ();
					//	Foreground.SetAsSource (gr);
					//}
					LogEntry le = lines[i+ScrollY]; 
					switch (le.Type) {
						case LogType.Low:
							gr.SetSource (Colors.DimGrey);
							break;
						case LogType.Normal:
							gr.SetSource (Colors.Grey);
							break;
						case LogType.High:
							gr.SetSource (Colors.White);
							break;
						case LogType.Debug:
							gr.SetSource (Colors.Yellow);
							break;
						case LogType.Warning:
							gr.SetSource (Colors.Orange);
							break;
						case LogType.Error:
							gr.SetSource (Colors.Red);
							break;
						case LogType.Custom1:
							gr.SetSource (Colors.Cyan);
							break;
						case LogType.Custom2:
							gr.SetSource (Colors.Green);
							break;
						case LogType.Custom3:
							gr.SetSource (Colors.LightPink);
							break;
					}
					gr.MoveTo (x, y + fe.Ascent);
					gr.ShowText (le.msg);
					y += fe.Height;
					gr.Fill ();
				}
			}
		}
		protected override void Dispose (bool disposing) {
			Lines = null;
		}

	}
}

