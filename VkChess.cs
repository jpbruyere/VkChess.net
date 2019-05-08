using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Crow;
using CVKL;
using Glfw;
using VK;

namespace vkChess
{
    class VkChess : CrowWin
    {
        static void Main(string[] args)
        {
            Instance.DebugUtils = true;
            Instance.Validation = true;

            using (VkChess app = new VkChess())
                app.Run();
        }

        protected override void configureEnabledFeatures(VkPhysicalDeviceFeatures available_features, ref VkPhysicalDeviceFeatures enabled_features)
        {
            base.configureEnabledFeatures(available_features, ref enabled_features);

            enabled_features.samplerAnisotropy = available_features.samplerAnisotropy;
            enabled_features.sampleRateShading = available_features.sampleRateShading;
            enabled_features.geometryShader = available_features.geometryShader;

            enabled_features.textureCompressionBC = available_features.textureCompressionBC;
        }

        Queue transferQ;
        protected override void createQueues()
        {
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

        public HostBuffer<Matrix4x4> instanceBuff;
        public List<Model.InstancedCmd> instancedCmds = new List<Model.InstancedCmd>();

        protected override void onLoad()
        {
            camera = new Camera(Utils.DegreesToRadians(45f), 1f, 0.1f, 16f);
            camera.SetRotation(0.5f, 0, 0);
            camera.SetPosition(0, -0.5f, 5);

            DeferredPbrRenderer.NUM_SAMPLES = VkSampleCountFlags.SampleCount4;
            DeferredPbrRenderer.DRAW_INSTACED = true;
            DeferredPbrRenderer.EnableTextureArray = true;

            renderer = new DeferredPbrRenderer(dev, swapChain, presentQueue, cubemapPathes[0], camera.NearPlane, camera.FarPlane);
            renderer.LoadModel(transferQ, modelPathes[0]);
            camera.Model = Matrix4x4.CreateScale(1f / Math.Max(Math.Max(renderer.modelAABB.Width, renderer.modelAABB.Height), renderer.modelAABB.Depth));

            crow.Load(@"ui/main.crow").DataSource = this;

            List<Matrix4x4> instDatas = new List<Matrix4x4>();
            for (int i = 0; i < 100; i++) {
                for (int j = 0; j < 100; j++) {
                    instDatas.Add(Matrix4x4.CreateTranslation(i*2, 0, j*2));
                }
            }

            instanceBuff = new HostBuffer<Matrix4x4>(dev, VkBufferUsageFlags.VertexBuffer, instDatas.ToArray());
            instancedCmds.Add(new Model.InstancedCmd { count = 10000, meshIdx = renderer.model.Meshes.IndexOf(renderer.model.FindNode("king").Mesh) });

        }
        protected override void recordDraw(CommandBuffer cmd, int imageIndex)
        {
            renderer.recordDraw(cmd, imageIndex, instanceBuff, instancedCmds.ToArray());
        }

        public override void UpdateView()
        {
            renderer.UpdateView(camera);
            updateViewRequested = false;
            if (renderer.shadowMapRenderer.updateShadowMap)
                renderer.shadowMapRenderer.update_shadow_map (cmdPool, instanceBuff, instancedCmds.ToArray());
        }
        protected override void OnResize()
        {
            renderer.Resize();
            base.OnResize();
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!isDisposed) {
                    renderer.Dispose();
                    instanceBuff.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        protected override void onKeyDown(Glfw.Key key, int scanCode, Modifier modifiers)
        {
            switch (key)
            {
                case Glfw.Key.F2:
                    crow.Load(@"ui/scene.crow").DataSource = this;
                    break;
                default:
                    base.onKeyDown(key, scanCode, modifiers);
                    break;
            }
        }

        #region crow

        public List<Model.Scene> Scenes => renderer.model.Scenes;

        public float Gamma
        {
            get { return renderer.matrices.gamma; }
            set
            {
                if (value == renderer.matrices.gamma)
                    return;
                renderer.matrices.gamma = value;
                NotifyValueChanged("Gamma", value);
                updateViewRequested = true;
            }
        }
        public float Exposure
        {
            get { return renderer.matrices.exposure; }
            set
            {
                if (value == renderer.matrices.exposure)
                    return;
                renderer.matrices.exposure = value;
                NotifyValueChanged("Exposure", value);
                updateViewRequested = true;
            }
        }
        public float IBLAmbient
        {
            get { return renderer.matrices.scaleIBLAmbient; }
            set
            {
                if (value == renderer.matrices.scaleIBLAmbient)
                    return;
                renderer.matrices.scaleIBLAmbient = value;
                NotifyValueChanged("IBLAmbient", value);
                updateViewRequested = true;
            }
        }
        public float LightStrength
        {
            get { return renderer.lights[renderer.lightNumDebug].color.X; }
            set
            {
                if (value == renderer.lights[renderer.lightNumDebug].color.X)
                    return;
                renderer.lights[renderer.lightNumDebug].color = new Vector4(value);
                NotifyValueChanged("LightStrength", value);
                renderer.uboLights.Update(renderer.lights);
            }
        }
        #endregion
    }
}
