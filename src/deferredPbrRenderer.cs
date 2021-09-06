using System;
using System.Numerics;
using System.Runtime.InteropServices;
using vke;
using vke.glTF;
using Vulkan;

namespace vkChess
{
	public class DeferredPbrRenderer : DeferredPbrRendererBase {
		public DeferredPbrRenderer (Device dev, SwapChain swapChain, PresentQueue presentQueue, string cubemapPath, float nearPlane, float farPlane) 
		: base (dev, swapChain, presentQueue, cubemapPath, nearPlane, farPlane) {
		}
	}
}
