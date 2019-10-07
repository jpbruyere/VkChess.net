// Copyright (c) 2019  Bruyère Jean-Philippe jp_bruyere@hotmail.com
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)
using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Crow;
using Crow.Cairo;
using vke;
using Vulkan;

namespace vkChess.net
{
	public class ColorWheel : Widget
	{
		public ColorWheel () {
		}

		struct PushConstant
		{
			public Vector2 resolution;
			public float innerRadius;
			public float outerRadius;
		}

		float hue, innerRadius, outerRadius;

		[DefaultValue (1.5f)]
		public float InnerRadius {
			get => innerRadius;
			set {
				if (innerRadius == value)
					return;
				innerRadius = value;
				NotifyValueChanged ("InnerRadius", innerRadius);
				RegisterForRedraw ();
			}
		}
		[DefaultValue (2.0f)]
		public float OuterRadius {
			get => outerRadius;
			set {
				if (outerRadius == value)
					return;
				outerRadius = value;
				NotifyValueChanged ("OuterRadius", outerRadius);
				RegisterForRedraw ();
			}
		}

		public float Hue {
			get => hue;
			set {
				if (hue == value)
					return;
				hue = value;
				NotifyValueChanged ("Hue", hue);
				RegisterForRedraw ();
			}
		}
		public override void onMouseDown (object sender, MouseButtonEventArgs e) {
			if (e.Button == MouseButton.Left)
				updateHueFromMousePos (e.Position);
		}
		public override void onMouseMove (object sender, MouseMoveEventArgs e) {
			if (e.Mouse.LeftButton == ButtonState.Pressed)
				updateHueFromMousePos (e.Position);
		}
		void updateHueFromMousePos (PointD mPos) {
			PointD m = ScreenPointToLocal (mPos);
			Rectangle r = ClientRectangle;
			PointD c = r.Center;
			m -= c;//get mouse pos relative to center
			if (m.Length < innerRadius * r.Center.X * 0.5)
				return;
			double angle = Math.Atan2 (m.X, -m.Y);
			if (angle < 0)
				angle += Math.PI * 2.0;
			Hue = (float)(angle / (Math.PI * 2.0));
		}
		protected override void onDraw (Context gr) {
			base.onDraw (gr);

			Rectangle r = ClientRectangle;

			VkCrowWindow backend = IFace.Backend as VkCrowWindow;

			GraphicPipelineConfig cfg = GraphicPipelineConfig.CreateDefault
				(VkPrimitiveTopology.TriangleList, VkSampleCountFlags.SampleCount1, false);
			cfg.RenderPass = new RenderPass (backend.Dev, VkFormat.B8g8r8a8Unorm);
			cfg.Layout = new PipelineLayout (backend.Dev, new VkPushConstantRange (VkShaderStageFlags.Fragment, (uint)sizeof (float) * 4));

			cfg.AddShader (VkShaderStageFlags.Vertex, "#vke.FullScreenQuad.vert.spv");
			cfg.AddShader (VkShaderStageFlags.Fragment, "#vkChess.net.colorWheel.frag.spv");

			using (GraphicPipeline pl = new GraphicPipeline (cfg)) {
				using (vke.Image img = new vke.Image (backend.Dev, VkFormat.B8g8r8a8Unorm, VkImageUsageFlags.ColorAttachment,
					VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent, (uint)r.Width, (uint)r.Height, VkImageType.Image2D,
					VkSampleCountFlags.SampleCount1, VkImageTiling.Linear)) {
					img.CreateView ();
					img.Map ();
					using (Surface surf = new ImageSurface (img.MappedData, Format.ARGB32,
						r.Width, r.Height, (int)img.GetSubresourceLayout ().rowPitch)) {
						using (FrameBuffer fb = new FrameBuffer (cfg.RenderPass, img.Width, img.Height, img)) {
							CommandBuffer cmd = backend.CmdPool.AllocateAndStart (VkCommandBufferUsageFlags.OneTimeSubmit);
							pl.RenderPass.Begin (cmd, fb);
							cmd.SetViewport (r.Width, r.Height);
							cmd.SetScissor (img.Width, img.Height);
							pl.Bind (cmd);
							PushConstant pc = new PushConstant {
								resolution = new Vector2 (r.Width, r.Height),
								innerRadius = innerRadius,
								outerRadius = outerRadius
							};
							cmd.PushConstant (pl, pc);
							cmd.Draw (3, 1, 0, 0);
							pl.RenderPass.End (cmd);
							backend.GraphicQueue.EndSubmitAndWait (cmd);
							cmd.Free ();
						}
						gr.SetSourceSurface (surf, 0, 0);
						gr.Paint ();
					}
				}
			}

			PointD c = r.Center;
			double radius = Math.Min (c.X, c.Y) * innerRadius * 0.5;
			double alpha = hue * 2.0 * Math.PI - Math.PI / 2.0;

			using (MeshPattern mp = new MeshPattern ()) {
				mp.BeginPatch ();
				PointD [] p = {
					getPoint (alpha, radius) + c,
					getPoint (alpha + 2.0 / 3.0 * Math.PI, radius) + c,
					getPoint (alpha + 4.0 / 3.0 * Math.PI, radius) + c,
					getPoint (alpha, radius) + c
				};
				mp.MoveTo (p [0]);
				mp.LineTo (p [1]);
				mp.LineTo (p [2]);

				//
				for (uint i = 0; i < 4; i++)
					mp.SetControlPoint (i, c);// p [i]);

				//mp.SetControlPoint (1, p [1]);

				/*mp.SetControlPoint (2, (p [1] - p[0]) * 0.7 + p[0]);
				mp.SetControlPoint (0, (p [2] - p [1]) * 0.5 + p [1]);
				mp.SetControlPoint (3, (p [2] - p [1]) * 0.5 + p [1]);
				mp.SetControlPoint (1, (p [2] - p [0]) * 0.7 + p [0]);*/

				/*mp.SetControlPoint (0, p [1]);
				mp.SetControlPoint (3, p [2]);

				mp.SetControlPoint (2, p [3]);*/
				//mp.SetControlPoint (1, p [1]);


				mp.SetCornerColor (0, Color.FromHSV (Hue));
				mp.SetCornerColor (1, Color.FromHSV (Hue, 1, 0));
				mp.SetCornerColor (2, Color.FromHSV (Hue, 0));

				mp.EndPatch ();
				gr.SetSource (mp);
				gr.Paint ();
			}

			double radIn = Math.Min (c.X, c.Y) * innerRadius * 0.57;
			double radOut = Math.Min (c.X, c.Y) * outerRadius * 0.5;

			gr.MoveTo (getPoint (alpha - 0.02, radIn) + c);
			gr.LineTo (getPoint (alpha - 0.02, radOut) + c);
			gr.LineTo (getPoint (alpha + 0.02, radOut) + c);
			gr.LineTo (getPoint (alpha + 0.02, radIn) + c);
			gr.ClosePath ();
			gr.SetSourceRGB (0, 0, 0);
			gr.LineWidth = 2;
			gr.StrokePreserve ();
			gr.LineWidth = 1;
			gr.SetSourceRGB (1, 1, 1);
			gr.Stroke ();

			//bmp.WriteToPng ("/home/jp/test.png");
		}
		PointD getPoint (double alpha, double radius) =>
			new PointD (Math.Cos (alpha), Math.Sin (alpha)) * radius;
	}
}
