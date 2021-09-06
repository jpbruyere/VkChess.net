using System;
using System.Numerics;
using System.Runtime.InteropServices;
using vke;
using vke.glTF;
using Vulkan;

namespace vkChess
{
	public class DeferredPbrRendererBase : IDisposable {
		Device dev;
		SwapChain swapChain;
		public PresentQueue presentQueue;

		public static int MAX_MATERIAL_COUNT = 8;
		public static VkSampleCountFlags NUM_SAMPLES = VkSampleCountFlags.SampleCount1;
		public static VkFormat HDR_FORMAT = VkFormat.R32g32b32a32Sfloat;
		public static VkFormat MRT_FORMAT = VkFormat.R32g32b32a32Sfloat;

		public enum DebugView {
			none,
			color,
			normal,
			pos,
			occlusion,
			emissive,
			metallic,
			roughness,
			depth,
			prefill,
			irradiance,
			shadowMap
		}

		public DebugView currentDebugView = DebugView.none;
		public int lightNumDebug = 0;
		public int debugMip = 0;
		public int debugFace = 0;

		public struct Matrices {
			public Matrix4x4 projection;
			public Matrix4x4 model;
			public Matrix4x4 view;
			public Vector4 camPos;
			public float exposure;
			public float gamma;
			public float prefilteredCubeMipLevels;
			public float scaleIBLAmbient;
		}
		public struct Light {
			public Vector4 position;
			public Vector4 color;
			public Matrix4x4 mvp;
		}

		public Matrices matrices = new Matrices {
			gamma = 1.2f,
			exposure = 2.0f,
			scaleIBLAmbient = 0.5f,
		};
		//public Light[] lights = {
		//    new Light {
		//        position = new Vector4(3.5f,12.5f,2,0f),
		//        color = new Vector4(1,0.8f,0.8f,1)
		//    },
		//    new Light {
		//        position = new Vector4(-3.5f,12.5f,2,0f),
		//        color = new Vector4(0.8f,0.8f,1,1)
		//    }
		//};
		public Light [] lights = {
			new Light {
				position = new Vector4(5.5f,12.5f,2,0f),
				color = new Vector4(1,1,1,1)
			},
			/*new Light {
				position = new Vector4(-5.5f,12.5f,-2,0f),
				color = new Vector4(1,1,1,1)
			}*/
		};

		//public DescriptorSetWrites UiImageUpdate => new DescriptorSetWrites (dsMain, descLayoutMain.Bindings [8]);

		const float lightMoveSpeed = 0.1f;

		FrameBuffers frameBuffers;
		Image gbColorRough, gbEmitMetal, gbN_AO, gbPos, hdrImg;

		public Image HDROutput => hdrImg;

		DescriptorPool descriptorPool;
		DescriptorSetLayout descLayoutMain, descLayoutGBuff;
		DescriptorSet dsMain, dsGBuff;

		public PipelineCache pipelineCache;
		Pipeline depthPrepassPipeline, gBuffPipeline, composePipeline, debugPipeline;

		public HostBuffer uboMatrices { get; private set; }
		public HostBuffer<Light> uboLights { get; private set; }

		RenderPass renderPass;

		public PbrModel model { get; private set; }
		public vke.Environment.EnvironmentCube envCube;
		public ShadowMapRenderer shadowMapRenderer;

		public BoundingBox modelAABB;

		const int SP_SKYBOX         = 0;
		const int SP_DEPTH_PREPASS	= 1;
		const int SP_MODELS         = 2;
		const int SP_COMPOSE        = 3;
		//const int SP_TONE_MAPPING   = 4;

		public Model.Scene mainScene;

		public DeferredPbrRendererBase (Device dev, SwapChain swapChain, PresentQueue presentQueue, string cubemapPath, float nearPlane, float farPlane) {
			this.dev = dev;
			this.swapChain = swapChain;
			this.presentQueue = presentQueue;            
			pipelineCache = new PipelineCache (dev);

			descriptorPool = new DescriptorPool (dev, 3,
				new VkDescriptorPoolSize (VkDescriptorType.UniformBuffer, 3),
				new VkDescriptorPoolSize (VkDescriptorType.CombinedImageSampler, 6),
				new VkDescriptorPoolSize (VkDescriptorType.InputAttachment, 5)
			);                

			uboMatrices = new HostBuffer (dev, VkBufferUsageFlags.UniformBuffer, matrices, true);
			uboLights = new HostBuffer<Light> (dev, VkBufferUsageFlags.UniformBuffer, lights, true);
			shadowMapRenderer = new ShadowMapRenderer (dev, this, 32);

			init (nearPlane, farPlane, cubemapPath);

			dev.WaitIdle ();
		}
		UInt32[] multiViewMasks = new UInt32[] {
			0b0001,
			0b0001,
			0b0011,
			0b0001
		};
		VkRenderPassMultiviewCreateInfo ciMultiview;

		void init_renderpass () {
			ciMultiview = VkRenderPassMultiviewCreateInfo.New();
			ciMultiview.subpassCount = 4;

			renderPass = new RenderPass (dev, NUM_SAMPLES);

			renderPass.AddAttachment (HDR_FORMAT, VkImageLayout.ShaderReadOnlyOptimal, NUM_SAMPLES, VkAttachmentLoadOp.Clear, VkAttachmentStoreOp.Store, VkImageLayout.ShaderReadOnlyOptimal);//hdr color
			renderPass.AddAttachment (dev.GetSuitableDepthFormat (), VkImageLayout.DepthStencilAttachmentOptimal, NUM_SAMPLES, VkAttachmentLoadOp.Clear, VkAttachmentStoreOp.DontCare);
			renderPass.AddAttachment (swapChain.ColorFormat, VkImageLayout.ColorAttachmentOptimal, NUM_SAMPLES, VkAttachmentLoadOp.Clear, VkAttachmentStoreOp.DontCare);//GBuff0 (color + roughness) and final color before resolve
			renderPass.AddAttachment (VkFormat.R8g8b8a8Unorm, VkImageLayout.ColorAttachmentOptimal, NUM_SAMPLES, VkAttachmentLoadOp.Clear, VkAttachmentStoreOp.DontCare);//GBuff1 (emit + metal)
			renderPass.AddAttachment (MRT_FORMAT, VkImageLayout.ColorAttachmentOptimal, NUM_SAMPLES, VkAttachmentLoadOp.Clear, VkAttachmentStoreOp.DontCare);//GBuff2 (normals + AO)
			renderPass.AddAttachment (MRT_FORMAT, VkImageLayout.ColorAttachmentOptimal, NUM_SAMPLES, VkAttachmentLoadOp.Clear, VkAttachmentStoreOp.DontCare);//GBuff3 (Pos + depth)			

			renderPass.ClearValues.Add (new VkClearValue { color = new VkClearColorValue (0.0f, 0.0f, 0.0f) });
			renderPass.ClearValues.Add (new VkClearValue { depthStencil = new VkClearDepthStencilValue (1.0f, 0) });
			renderPass.ClearValues.Add (new VkClearValue { color = new VkClearColorValue (0.0f, 0.0f, 0.0f) });
			renderPass.ClearValues.Add (new VkClearValue { color = new VkClearColorValue (0.0f, 0.0f, 0.0f) });
			renderPass.ClearValues.Add (new VkClearValue { color = new VkClearColorValue (0.0f, 0.0f, 0.0f) });
			renderPass.ClearValues.Add (new VkClearValue { color = new VkClearColorValue (0.0f, 0.0f, 0.0f) });

			SubPass[] subpasses = { new SubPass (), new SubPass (), new SubPass (), new SubPass () };
			//skybox
			subpasses[SP_SKYBOX].AddColorReference (0, VkImageLayout.ColorAttachmentOptimal);

			//depth pre pass
			subpasses [SP_DEPTH_PREPASS].SetDepthReference (1, VkImageLayout.DepthStencilAttachmentOptimal);
			subpasses [SP_DEPTH_PREPASS].AddPreservedReference (0);
			//models
			subpasses [SP_MODELS].AddColorReference (new VkAttachmentReference (2, VkImageLayout.ColorAttachmentOptimal),
									new VkAttachmentReference (3, VkImageLayout.ColorAttachmentOptimal),
									new VkAttachmentReference (4, VkImageLayout.ColorAttachmentOptimal),
									new VkAttachmentReference (5, VkImageLayout.ColorAttachmentOptimal));
			subpasses[SP_MODELS].SetDepthReference (1, VkImageLayout.DepthStencilAttachmentOptimal);
			subpasses[SP_MODELS].AddPreservedReference (0);

			//compose
			subpasses[SP_COMPOSE].AddColorReference (0, VkImageLayout.ColorAttachmentOptimal);
			subpasses[SP_COMPOSE].AddInputReference (new VkAttachmentReference (2, VkImageLayout.ShaderReadOnlyOptimal),
									new VkAttachmentReference (3, VkImageLayout.ShaderReadOnlyOptimal),
									new VkAttachmentReference (4, VkImageLayout.ShaderReadOnlyOptimal),
									new VkAttachmentReference (5, VkImageLayout.ShaderReadOnlyOptimal));
			//tone mapping
			/*subpasses[SP_TONE_MAPPING].AddColorReference ((NUM_SAMPLES == VkSampleCountFlags.SampleCount1) ? 0u : 2u, VkImageLayout.ColorAttachmentOptimal);
			subpasses[SP_TONE_MAPPING].AddInputReference (new VkAttachmentReference (6, VkImageLayout.ShaderReadOnlyOptimal));
			if (NUM_SAMPLES != VkSampleCountFlags.SampleCount1)
				subpasses[SP_TONE_MAPPING].AddResolveReference (0, VkImageLayout.ColorAttachmentOptimal);*/

			renderPass.AddSubpass (subpasses);

			renderPass.AddDependency (Vk.SubpassExternal, SP_SKYBOX,
				VkPipelineStageFlags.BottomOfPipe, VkPipelineStageFlags.ColorAttachmentOutput,
				VkAccessFlags.MemoryRead, VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite);
			renderPass.AddDependency (SP_SKYBOX, SP_DEPTH_PREPASS,
				VkPipelineStageFlags.ColorAttachmentOutput, VkPipelineStageFlags.FragmentShader,
				VkAccessFlags.ColorAttachmentWrite, VkAccessFlags.ShaderRead);
			renderPass.AddDependency (SP_DEPTH_PREPASS, SP_MODELS,
				VkPipelineStageFlags.EarlyFragmentTests, VkPipelineStageFlags.FragmentShader,
				VkAccessFlags.DepthStencilAttachmentWrite, VkAccessFlags.ShaderRead);
			renderPass.AddDependency (SP_MODELS, SP_COMPOSE,
				VkPipelineStageFlags.ColorAttachmentOutput, VkPipelineStageFlags.FragmentShader,
				VkAccessFlags.ColorAttachmentWrite, VkAccessFlags.ShaderRead);
			renderPass.AddDependency (SP_COMPOSE, Vk.SubpassExternal,
				VkPipelineStageFlags.ColorAttachmentOutput, VkPipelineStageFlags.Transfer,
				VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite, VkAccessFlags.TransferRead);				
		}

		void init (float nearPlane, float farPlane, string cubemapPath) {
			init_renderpass ();

			descLayoutMain = new DescriptorSetLayout (dev,
				new VkDescriptorSetLayoutBinding (0, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, VkDescriptorType.UniformBuffer),//matrices and params
				new VkDescriptorSetLayoutBinding (1, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),//irradiance
				new VkDescriptorSetLayoutBinding (2, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),//prefil
				new VkDescriptorSetLayoutBinding (3, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),//lut brdf
				new VkDescriptorSetLayoutBinding (4, VkShaderStageFlags.Fragment, VkDescriptorType.UniformBuffer),//lights
				new VkDescriptorSetLayoutBinding (5, VkShaderStageFlags.Fragment, VkDescriptorType.UniformBuffer),//materials
				new VkDescriptorSetLayoutBinding (6, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler),//shadow map
				new VkDescriptorSetLayoutBinding (7, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler)//texture array
				//new VkDescriptorSetLayoutBinding (8, VkShaderStageFlags.Fragment, VkDescriptorType.CombinedImageSampler)
				);//uiImage

			descLayoutGBuff = new DescriptorSetLayout (dev,
				new VkDescriptorSetLayoutBinding (0, VkShaderStageFlags.Fragment, VkDescriptorType.InputAttachment),//color + roughness
				new VkDescriptorSetLayoutBinding (1, VkShaderStageFlags.Fragment, VkDescriptorType.InputAttachment),//emit + metal
				new VkDescriptorSetLayoutBinding (2, VkShaderStageFlags.Fragment, VkDescriptorType.InputAttachment),//normals + AO
				new VkDescriptorSetLayoutBinding (3, VkShaderStageFlags.Fragment, VkDescriptorType.InputAttachment),//Pos + depth
				new VkDescriptorSetLayoutBinding (4, VkShaderStageFlags.Fragment, VkDescriptorType.InputAttachment));//hdr

			descLayoutMain.SetName ("main");
			descLayoutGBuff.SetName ("GBuff");

			ciMultiview.pViewMasks = multiViewMasks.Pin ();
			//renderPass.PNext = new PNextNode<VkRenderPassMultiviewCreateInfo> (ciMultiview);

			using (GraphicPipelineConfig cfg = GraphicPipelineConfig.CreateDefault (VkPrimitiveTopology.TriangleList, NUM_SAMPLES)) {
				
				cfg.rasterizationState.cullMode = VkCullModeFlags.None;

				if (NUM_SAMPLES != VkSampleCountFlags.SampleCount1) {
					cfg.multisampleState.sampleShadingEnable = true;
					cfg.multisampleState.minSampleShading = 0.5f;
				}
				cfg.Cache = pipelineCache;
				cfg.Layout = new PipelineLayout (dev,
					new VkPushConstantRange[] {
					new VkPushConstantRange (VkShaderStageFlags.Vertex, (uint)Marshal.SizeOf<Matrix4x4> ()),
					new VkPushConstantRange (VkShaderStageFlags.Fragment, sizeof (int), 64)},
					descLayoutMain, descLayoutGBuff);

				cfg.RenderPass = renderPass;
				//cfg.blendAttachments.Add (new VkPipelineColorBlendAttachmentState (false));

				cfg.AddVertexBinding<PbrModelTexArray.Vertex> (0);
				cfg.AddVertexAttributes (0, VkFormat.R32g32b32Sfloat, VkFormat.R32g32b32Sfloat, VkFormat.R32g32Sfloat, VkFormat.R32g32Sfloat);

				cfg.AddVertexBinding<VkChess.InstanceData> (1, VkVertexInputRate.Instance);
				cfg.AddVertexAttributes (1, VkFormat.R32g32b32a32Sfloat, VkFormat.R32g32b32a32Sfloat, VkFormat.R32g32b32a32Sfloat, VkFormat.R32g32b32a32Sfloat, VkFormat.R32g32b32a32Sfloat);

				using (SpecializationInfo constants = new SpecializationInfo (
							new SpecializationConstant<float> (0, nearPlane),
							new SpecializationConstant<float> (1, farPlane),
							new SpecializationConstant<int> (2, MAX_MATERIAL_COUNT))) {
					cfg.AddShader (dev, VkShaderStageFlags.Vertex, "#vkChess.net.GBuffPbrInstanced.vert.spv");

					cfg.SubpassIndex = SP_DEPTH_PREPASS;
					depthPrepassPipeline = new GraphicPipeline (cfg, "Depth prepass");

					cfg.depthStencilState.depthCompareOp = VkCompareOp.Equal;
					cfg.blendAttachments.Add (new VkPipelineColorBlendAttachmentState (false));
					cfg.blendAttachments.Add (new VkPipelineColorBlendAttachmentState (false));
					cfg.blendAttachments.Add (new VkPipelineColorBlendAttachmentState (false));

					cfg.SubpassIndex = SP_MODELS;

					cfg.AddShader (dev, VkShaderStageFlags.Fragment, "#vkChess.net.GBuffPbrTexArray.frag.spv", constants);

					gBuffPipeline = new GraphicPipeline (cfg, "GBuff");
				}
				cfg.rasterizationState.cullMode = VkCullModeFlags.Front;
				//COMPOSE PIPELINE
				cfg.blendAttachments.Clear ();
				cfg.blendAttachments.Add (new VkPipelineColorBlendAttachmentState (false));

				cfg.ResetShadersAndVerticesInfos ();

				cfg.SubpassIndex = SP_COMPOSE;
				cfg.Layout = gBuffPipeline.Layout;
				cfg.depthStencilState.depthTestEnable = false;
				cfg.depthStencilState.depthWriteEnable = false;
				using (SpecializationInfo constants = new SpecializationInfo (
					new SpecializationConstant<uint> (0, (uint)lights.Length),
					new SpecializationConstant<float> (1, 0.25f))) {
					cfg.AddShader (dev, VkShaderStageFlags.Vertex, "#vke.FullScreenQuad.vert.spv");
					cfg.AddShader (dev, VkShaderStageFlags.Fragment, "#vkChess.net.compose_with_shadows.frag.spv", constants);
					composePipeline = new GraphicPipeline (cfg, "compose");
				}
				//DEBUG DRAW use subpass of compose
				cfg.ReplaceShader (1, new ShaderInfo (dev, VkShaderStageFlags.Fragment, "#vkChess.net.show_gbuff.frag.spv"));
				cfg.SubpassIndex = SP_COMPOSE;
				debugPipeline = new GraphicPipeline (cfg, "debug");
				//TONE MAPPING
				/*cfg.ReplaceShader (1, new ShaderInfo (dev, VkShaderStageFlags.Fragment, "#vkChess.net.tone_mapping.frag.spv"));
				cfg.SubpassIndex = SP_TONE_MAPPING;
				toneMappingPipeline = new GraphicPipeline (cfg, "tonne mapping");*/
			}

			dsMain = descriptorPool.Allocate (descLayoutMain);
			dsGBuff = descriptorPool.Allocate (descLayoutGBuff);

			envCube = new vke.Environment.EnvironmentCube (cubemapPath, gBuffPipeline.Layout, presentQueue, renderPass);

			matrices.prefilteredCubeMipLevels = envCube.prefilterCube.CreateInfo.mipLevels;

			DescriptorSetWrites dsMainWrite = new DescriptorSetWrites (dsMain, descLayoutMain.Bindings.GetRange (0, 5).ToArray ());
			dsMainWrite.Write (dev, 
				uboMatrices.Descriptor,
				envCube.irradianceCube.Descriptor,
				envCube.prefilterCube.Descriptor,
				envCube.lutBrdf.Descriptor,
				uboLights.Descriptor);

			dsMainWrite = new DescriptorSetWrites (dsMain, descLayoutMain.Bindings[6]);
			dsMainWrite.Write (dev, shadowMapRenderer.shadowMap.Descriptor);

			multiViewMasks.Unpin ();
			
		}

		public void LoadModel (Queue transferQ, string path) {
			dev.WaitIdle ();

			model?.Dispose ();
			model = new PbrModelTexArray (transferQ, path);

			DescriptorSetWrites uboUpdate = new DescriptorSetWrites (dsMain, descLayoutMain.Bindings[5], descLayoutMain.Bindings[7]);
			uboUpdate.Write (dev, model.materialUBO.Descriptor, (model as PbrModelTexArray).texArray.Descriptor);

			modelAABB = model.DefaultScene.AABB;
		}

		public void recordDraw (PrimaryCommandBuffer cmd, int imageIndex, vke.Buffer instanceBuf = null, params Model.InstancedCmd[] instances) {
			FrameBuffer fb = frameBuffers[imageIndex];
			renderPass.Begin(cmd, fb);

			cmd.SetViewport(fb.Width, fb.Height);
			cmd.SetScissor(fb.Width, fb.Height);

			cmd.BindDescriptorSet(gBuffPipeline.Layout, dsMain);

			envCube.RecordDraw(cmd);
			renderPass.BeginSubPass(cmd);

			if (model != null) {
				model.Bind (cmd);

				depthPrepassPipeline.Bind (cmd);
				if (instanceBuf != null)
					model.Draw (cmd, gBuffPipeline.Layout, instanceBuf, false, instances);

				renderPass.BeginSubPass (cmd);
				gBuffPipeline.Bind(cmd);
				
				if (instanceBuf != null)
					model.Draw(cmd, gBuffPipeline.Layout, instanceBuf, false, instances);
			}
			renderPass.BeginSubPass(cmd);

			cmd.BindDescriptorSet(composePipeline.Layout, dsGBuff, 1);

			if (currentDebugView == DebugView.none)
				composePipeline.Bind(cmd);
			else {
				debugPipeline.Bind(cmd);
				uint debugValue = (uint)currentDebugView - 1;
				if (currentDebugView == DebugView.shadowMap)
					debugValue += (uint)((lightNumDebug << 8));
				else
					debugValue += (uint)((debugFace << 8) + (debugMip << 16));
				cmd.PushConstant(debugPipeline.Layout, VkShaderStageFlags.Fragment, debugValue, (uint)Marshal.SizeOf<Matrix4x4>());
			}

			cmd.Draw(3, 1, 0, 0);

			/*renderPass.BeginSubPass(cmd);
			toneMappingPipeline.Bind(cmd);
			cmd.Draw(3, 1, 0, 0);*/

			renderPass.End(cmd);
		}

		public void MoveLight (Vector4 dir) {
			lights[lightNumDebug].position += dir * lightMoveSpeed;
			shadowMapRenderer.updateShadowMap = true;
		}

		#region update
		public void UpdateView (Camera camera) {
			camera.AspectRatio = (float)swapChain.Width / swapChain.Height;

			matrices.projection = camera.Projection;
			matrices.view = camera.View;
			matrices.model = camera.Model;

			Matrix4x4.Invert (camera.View, out Matrix4x4 inv);
			matrices.camPos = new Vector4 (inv.M41, inv.M42, inv.M43, 0);

			uboMatrices.Update (matrices, (uint)Marshal.SizeOf<Matrices> ());
			shadowMapRenderer.updateShadowMap = true;
		}

		#endregion

		void createGBuff () {
			gbColorRough?.Dispose ();
			gbEmitMetal?.Dispose ();
			gbN_AO?.Dispose ();
			gbPos?.Dispose ();
			hdrImg?.Dispose ();

			uint layerCount = 1;

			gbColorRough = new Image (dev, swapChain.ColorFormat, VkImageUsageFlags.InputAttachment | VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransientAttachment, VkMemoryPropertyFlags.DeviceLocal, swapChain.Width, swapChain.Height, VkImageType.Image2D, NUM_SAMPLES, VkImageTiling.Optimal, 1, layerCount);
			gbEmitMetal = new Image (dev, VkFormat.R8g8b8a8Unorm, VkImageUsageFlags.InputAttachment | VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransientAttachment, VkMemoryPropertyFlags.DeviceLocal, swapChain.Width, swapChain.Height, VkImageType.Image2D, NUM_SAMPLES, VkImageTiling.Optimal, 1, layerCount);
			gbN_AO = new Image (dev, MRT_FORMAT, VkImageUsageFlags.InputAttachment | VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransientAttachment, VkMemoryPropertyFlags.DeviceLocal, swapChain.Width, swapChain.Height, VkImageType.Image2D, NUM_SAMPLES, VkImageTiling.Optimal, 1, layerCount);
			gbPos = new Image (dev, MRT_FORMAT, VkImageUsageFlags.InputAttachment | VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransientAttachment, VkMemoryPropertyFlags.DeviceLocal, swapChain.Width, swapChain.Height, VkImageType.Image2D, NUM_SAMPLES, VkImageTiling.Optimal, 1, layerCount);
			hdrImg = new Image (dev, HDR_FORMAT, VkImageUsageFlags.InputAttachment | VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransientAttachment | VkImageUsageFlags.Sampled, VkMemoryPropertyFlags.DeviceLocal | VkMemoryPropertyFlags.DeviceLocal, swapChain.Width, swapChain.Height, VkImageType.Image2D, NUM_SAMPLES, VkImageTiling.Optimal, 1, 1);

			gbColorRough.CreateView ();
			gbColorRough.CreateSampler ();
			gbColorRough.Descriptor.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
			gbEmitMetal.CreateView ();
			gbEmitMetal.CreateSampler ();
			gbEmitMetal.Descriptor.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
			gbN_AO.CreateView ();
			gbN_AO.CreateSampler ();
			gbN_AO.Descriptor.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
			gbPos.CreateView ();
			gbPos.CreateSampler ();
			gbPos.Descriptor.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;
			hdrImg.CreateView ();
			hdrImg.CreateSampler ();
			hdrImg.Descriptor.imageLayout = VkImageLayout.ShaderReadOnlyOptimal;

			DescriptorSetWrites uboUpdate = new DescriptorSetWrites (descLayoutGBuff);
			uboUpdate.Write (dev, dsGBuff,    gbColorRough.Descriptor,
										gbEmitMetal.Descriptor,
										gbN_AO.Descriptor,
										gbPos.Descriptor,
										hdrImg.Descriptor);
			gbColorRough.SetName ("GBuffColorRough");
			gbEmitMetal.SetName ("GBuffEmitMetal");
			gbN_AO.SetName ("GBuffN");
			gbPos.SetName ("GBuffPos");
			hdrImg.SetName ("HDRimg");
		}

		public void Resize () {
			frameBuffers?.Dispose ();
			frameBuffers = new FrameBuffers ();

			createGBuff ();

			for (int i = 0; i < swapChain.ImageCount; ++i) {
				frameBuffers.Add (new FrameBuffer (renderPass, swapChain.Width, swapChain.Height, 1, new Image [] {
					hdrImg, null, gbColorRough, gbEmitMetal, gbN_AO, gbPos}));
			}
			//frameBuffers = renderPass.CreateFrameBuffers (swapChain);
		}

		public void Dispose () {
			dev.WaitIdle ();
			
			frameBuffers?.Dispose ();

			gbColorRough.Dispose ();
			gbEmitMetal.Dispose ();
			gbN_AO.Dispose ();
			gbPos.Dispose ();
			hdrImg.Dispose ();

			gBuffPipeline.Dispose ();
			composePipeline.Dispose ();
			debugPipeline.Dispose ();
			depthPrepassPipeline.Dispose ();

			descLayoutMain.Dispose ();            
			descLayoutGBuff.Dispose ();

			uboMatrices.Dispose ();
			uboLights.Dispose ();
			model.Dispose ();
			envCube.Dispose ();
			shadowMapRenderer.Dispose ();

			descriptorPool.Dispose ();
		}
	}
}
