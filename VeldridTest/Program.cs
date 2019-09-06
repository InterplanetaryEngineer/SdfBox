﻿
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Generator;
using System.Text;

/* TODO
 * Modeling
 * Moar lights
 * Moar speed
 */

namespace SDFbox
{
    class Program
    {
        public static Sdl2Window window;
        public static GraphicsDevice device;
        public static ResourceFactory factory;
        public static ImGuiRenderer imGuiRenderer;
        public static CommandList commandList;

        public static OctData model;
        static Texture renderTexture;

        static ComputeUnit compute;
        static VertFragUnit renderer;
        static VertFragUnit uiRenderer;
#if DEBUG
        public const bool DebugMode = true;
#else
        public const bool DebugMode = false;
#endif

        static void Main(string[] args)
        {
            window = Utilities.MakeWindow(720, 720);
            Logic.Init(window);
            window.Resized += Resize;

            FPS fpsCounter = new FPS();
            device = VeldridStartup.CreateGraphicsDevice(window, new GraphicsDeviceOptions(DebugMode, null, false, ResourceBindingModel.Improved));
            CreateResources(args[0]);
            DateTime start = DateTime.Now;
            TimeSpan delta = TimeSpan.FromSeconds(0);
            Logic.Heading = Logic.Heading;
            Logic.Position = Logic.Position;
            window.WindowState = WindowState.Maximized;

            while (window.Exists) {
                delta = DateTime.Now - start;
                start = DateTime.Now;

                var input = window.PumpEvents();
                Logic.Update(delta, input);
                Logic.MakeGUI(fpsCounter.Frames);
                Draw();
                fpsCounter.Frame();
            }
            ImGui.DestroyContext();
            DisposeResources();
        }

        static void Draw()
        {
            device.UpdateBuffer(compute.Buffer("info"), 0, Logic.State);
            device.UpdateBuffer(renderer.Buffer("info"), 0, Logic.DrawState);

            Logic.vm?.Inside(Vector3.Zero, 0);

            var cl = commandList;
            /**
                if (Generator.GpuGenerator.compute != null)
                Generator.GpuGenerator.DistanceAt(Logic.State.position); 39712
                var gcmp = GpuGenerator.compute;
                device.UpdateBuffer(gcmp.Buffer("info"), 0, new Vector4(0, 0, 0, 0));
                gcmp.Update(factory);
            */

            cl.Begin();
            compute.DispatchSized(cl, (uint) window.Width, (uint) window.Height, 1);

            cl.SetFramebuffer(device.SwapchainFramebuffer);
            cl.SetFullViewports();
            cl.ClearColorTarget(0, RgbaFloat.Black);
            renderer.Draw(cl, indexCount: 4, instanceCount: 1);
            if (Logic.debugGizmos) {
                device.UpdateBuffer(uiRenderer.VertexBuffer, 0, Logic.DebugUtils);
                uiRenderer.Draw(cl, indexCount: 6, instanceCount: 1);
            }

            imGuiRenderer.Render(device, commandList);

            cl.End();
            device.SubmitCommands(commandList);
            device.SwapBuffers(device.MainSwapchain);
        }


        static void CreateResources(string path)
        {
            factory = device.ResourceFactory;
            commandList = factory.CreateCommandList();
            Vertex[] quadVertices = Logic.ScreenQuads;
            ushort[] quadIndices = { 0, 1, 2, 3 };

            imGuiRenderer = new ImGuiRenderer(device,
                device.SwapchainFramebuffer.OutputDescription, window.Width, window.Height);


            renderTexture = MakeTexture(Round(window.Width, Logic.GroupSize), Round(window.Height, Logic.GroupSize));//var resultTBuffer = factory.CreateTextureView(new TextureViewDescription(renderTexture));
            var drawUBuffer = MakeBuffer(new Info[] { Logic.State }, BufferUsage.UniformBuffer);

            var renderDesc = new VertFragUnit.Description() {
                Vertex = LoadShader(ShaderStages.Vertex),
                Fragment = LoadShader("DisplayFrag", ShaderStages.Fragment),

                VertexBuffer = MakeBuffer(quadVertices, BufferUsage.VertexBuffer),
                IndexBuffer = MakeBuffer(quadIndices, BufferUsage.IndexBuffer),
                Topology = PrimitiveTopology.TriangleStrip,
                Output = device.SwapchainFramebuffer.OutputDescription
            };
            renderDesc.AddResource("texture", ResourceKind.TextureReadOnly, renderTexture);
            renderDesc.AddResource("info", ResourceKind.UniformBuffer, drawUBuffer);
            renderer = new VertFragUnit(factory, renderDesc);

            model = Logic.MakeData(path);
            var infoUBuffer = MakeBuffer(new Info[] { Logic.State }, BufferUsage.UniformBuffer);

            var compDesc = new ComputeUnit.Description(LoadShader("Compute", ShaderStages.Compute)) {
                xGroupSize = 24,
                yGroupSize = 24
            };

            compDesc.AddResource("sampler", ResourceKind.Sampler, device.LinearSampler);
            //compDesc.AddResource("values", ResourceKind.StructuredBufferReadOnly, model.ValueBuffer());
            compDesc.AddResource("values", ResourceKind.TextureReadOnly, model.ValueTexture());
            compDesc.AddResource("data", ResourceKind.StructuredBufferReadOnly, model.StructBuffer());
            compDesc.AddResource("info", ResourceKind.UniformBuffer, infoUBuffer);
            compDesc.AddResource("result", ResourceKind.TextureReadWrite, renderTexture);
            compute = new ComputeUnit(factory, compDesc);


            ushort[] debugIndices = { 0, 1, 2, 3, 4, 5 };
            var uiDesc = new VertFragUnit.Description() {
                Vertex = LoadShader(ShaderStages.Vertex),
                Fragment = LoadShader("Plain", ShaderStages.Fragment),
                VertexBuffer = MakeBuffer(Logic.DebugUtils, BufferUsage.VertexBuffer),
                IndexBuffer = MakeBuffer(debugIndices, BufferUsage.IndexBuffer),
                Topology = PrimitiveTopology.LineList,
                Output = device.SwapchainFramebuffer.OutputDescription
            };
            uiRenderer = new VertFragUnit(factory, uiDesc);
        }
        public static void Load(string path)
        {
            model = Logic.MakeData(path);
            compute["data"] = model.StructBuffer();
            compute["values"] = model.ValueTexture();
            compute.Update(factory);
        }
        public static DeviceBuffer MakeBuffer<T>(T[] data, BufferUsage usage, uint size = 0, bool raw = false) where T : struct
        {
            BufferDescription description;
            uint structuredStride = 0;
            uint singleSize = (uint) Marshal.SizeOf(data[0]);

            if (usage == BufferUsage.StructuredBufferReadOnly || usage == BufferUsage.StructuredBufferReadWrite)
                structuredStride = singleSize;
            if (size == 0)
                size = (uint) data.Length * singleSize;

            description = new BufferDescription(size, usage, structuredStride, raw);
            DeviceBuffer newBuffer = factory.CreateBuffer(description);

            device.UpdateBuffer(newBuffer, 0, data);
            return newBuffer;
        }
        public static DeviceBuffer MakeEmptyBuffer<T>(BufferUsage usage, uint count) where T : struct
        {
            BufferDescription description;
            uint structuredStride = 0;
            uint singleSize = (uint) Marshal.SizeOf(new T());
            uint size = count * singleSize;

            if (usage == BufferUsage.StructuredBufferReadOnly || usage == BufferUsage.StructuredBufferReadWrite)
                structuredStride = singleSize;

            description = new BufferDescription(size, usage, structuredStride);
            DeviceBuffer newBuffer = factory.CreateBuffer(description);

            return newBuffer;
        }
        static Texture MakeTexture(uint x, uint y, PixelFormat format = PixelFormat.R32_G32_B32_A32_Float)
        {
            TextureDescription tDesc = new TextureDescription {
                Type = TextureType.Texture2D,
                ArrayLayers = 1,
                Format = format,
                Width = x,
                Height = y,
                Depth = 1,
                Usage = TextureUsage.Storage | TextureUsage.Sampled,
                MipLevels = 1,
                SampleCount = TextureSampleCount.Count1
            };
            
            return factory.CreateTexture(tDesc);
        }
        static Sampler MakeSampler()
        {
            /*
            SamplerDescription desc = new SamplerDescription() {
                AddressModeU = SamplerAddressMode.Wrap,
                AddressModeV = SamplerAddressMode.Wrap,
                AddressModeW = SamplerAddressMode.Wrap,
                Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
                
            };*/
            return factory.CreateSampler(SamplerDescription.Linear);
        }
        public static Shader LoadShader(string name, ShaderStages stage, params string[] include)
        {
            string extension = GraphicsExtension();
            string entryPoint = "";
            switch (stage) {
                case ShaderStages.Vertex:
                    entryPoint = "VS";
                    break;
                case ShaderStages.Fragment:
                    entryPoint = "FS";
                    break;
                case ShaderStages.Compute:
                    entryPoint = "main";
                    break;
            }
            string path = Path.Combine(AppContext.BaseDirectory, "Shaders", $"{name}.{extension}");
            Debug.WriteLine("Loading shader: " + path);

            List<byte> shaderBytes = new List<byte>();
            foreach (string inc in include) {
                string incpath = Path.Combine(AppContext.BaseDirectory, "Shaders", $"{name}.{extension}");
                shaderBytes.AddRange(File.ReadAllBytes(incpath));
            }
            shaderBytes.AddRange(File.ReadAllBytes(path));

            var desc = new ShaderDescription(stage, shaderBytes.ToArray(), entryPoint, DebugMode);

            return factory.CreateShader(desc);

            string GraphicsExtension()
            {
                switch (device.BackendType) {
                    case GraphicsBackend.Direct3D11:
                        return "hlsl";
                    case GraphicsBackend.Vulkan:
                        return "spv";
                    case GraphicsBackend.OpenGL:
                        return "glsl";
                    case GraphicsBackend.Metal:
                        return "metallib";
                    default: throw new InvalidOperationException();
                }
            }
        }
        static Shader LoadShader(ShaderStages stage)
        {
            return LoadShader(stage.ToString(), stage);
        }

        public static void ToggleFullscreen()
        {
            if (window.WindowState == WindowState.BorderlessFullScreen)
                window.WindowState = WindowState.Normal;
            else
                window.WindowState = WindowState.BorderlessFullScreen;
        }
        static void Resize()
        {
            Logic.State.screen_size = ScreenSize;
            imGuiRenderer.WindowResized(window.Width, window.Height);
            device.MainSwapchain.Resize((uint) window.Width, (uint) window.Height);
            Texture newTarget = MakeTexture(Round(window.Width, Logic.GroupSize), Round(window.Height, Logic.GroupSize));
            compute["result"] = newTarget;
            renderer["texture"] = newTarget;
            compute.Update(factory);
            renderer.Update(factory);
        }
        static void DisposeResources()
        {
            renderer.Dispose();
            compute.Dispose();

            commandList.Dispose();
            device.Dispose();
        }

        public static Vector2 ScreenSize {
            get {
                return new Vector2(window.Width, window.Height);
            }
            set {
                window.Width = (int) value.X;
                window.Height = (int) value.Y;
            }
        }
        public static uint Round(float val, int blocksize)
        {
            return (uint) (val + blocksize - val % blocksize);
        }
    }



    struct Vertex
    {
        public Vector3 Position; // This is the position, in normalized device coordinates.
        public RgbaFloat Color; // This is the color of the vertex.
        public Vertex(RgbaFloat color, float x, float y, float z = 0)
        {
            Position = new Vector3(x, y, z);
            Color = color;
        }
        public Vertex(RgbaFloat color, double x, double y, double z)
        {
            Position = new Vector3((float) x, (float) y, (float) z);
            Color = color;
        }
    }

    ///*
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    [Serializable]
    struct OctS
    {
        public Vector3 lower;
        public float scale;

        public int Parent;
        public int empty;
        public int children;
        //public Int8 children;
        //*/
        /*
        public OctS(int Parent, Int8 children, Vector8 verts, Vector3 lower, Vector3 higher) {
            this.Parent = Parent;
            this.lower = lower;
            this.higher = higher;

            this.children = children;

            this.verts = verts;

            empty = 1;
            if (vertsL.X <= 0 || vertsL.X <= 0 || vertsL.W <= 0 || vertsL.Z <= 0)
                empty = 0;
            if (vertsH.X <= 0 || vertsH.X <= 0 || vertsH.W <= 0 || vertsH.Z <= 0)
                empty = 0;
        }//*/
        public Vector3 higher {
            get {
                return lower + Vector3.One * scale;
            }
        }
        public OctS(int Parent, int children, float[] verts, Vector3 lower, float scale)
        {
            this.Parent = Parent;
            this.lower = lower;
            this.scale = scale;

            this.children = children;

            empty = 1;
            foreach (float x in verts) {
                if (x <= 0)
                    empty = 0;
            }
        }
        /*
        public static void Serialize(OctS[] octs, BinaryWriter writer)
        {
            foreach (OctS current in octs) {
                writer.Write(current.Parent);
                writer.Write(current.empty);
                writer.Write(current.children);
                current.verts.Serialize(writer);
            }
        }

        public static OctS[] Deserialize(BinaryReader reader)
        {
            List<OctS> read = new List<OctS>();
            while (reader.BaseStream.Position != reader.BaseStream.Length) {
                read.Add(new OctS() {
                    Parent = reader.ReadInt32(),
                    empty = reader.ReadInt32(),
                    children = reader.ReadInt32(),
                    verts = Half8.Deserialize(reader)
                });
            }
            Rectify(0, Vector3.Zero, 1);

            return read.ToArray();

            void Rectify(int p, Vector3 lower, float scale)
            {
                OctS current = read[p];
                current.lower = lower;
                current.scale = scale;
                read[p] = current;
                if (current.children == -1)
                    return;

                scale /= 2;
                for (int i = 0; i < 8; i++) {
                    Rectify(current.children + i, lower + SdfMath.split(i).Vector * scale, scale);
                }
            }
        }
        */
    }


    
    [StructLayout(LayoutKind.Sequential, Pack = 16, Size = 32)]
    struct Vector8
    {
        public Single S;
        public Single T;
        public Single U;
        public Single V;
        public Single W;
        public Single X;
        public Single Y;
        public Single Z;

        public Vector8(float s, float t, float u, float v, float w, float x, float y, float z)
        {
            S = s;
            T = t;
            U = u;
            V = v;
            W = w;
            X = x;
            Y = y;
            Z = z;
        }
        public Vector8(float[] d)
        {
            S = d[0];
            T = d[1];
            U = d[2];
            V = d[3];
            W = d[4];
            X = d[5];
            Y = d[6];
            Z = d[7];
        }

        public float[] Array {
            get {
                return new float[] { S, T, U, V, W, X, Y, Z };
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(S);
            writer.Write(T);
            writer.Write(U);
            writer.Write(V);
            writer.Write(W);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
        }
        public static Vector8 Deserialize(BinaryReader reader)
        {
            return new Vector8(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16, Size = 16)]
    struct Byte8
    {
        public byte S;
        public byte T;
        public byte U;
        public byte V;
        public byte W;
        public byte X;
        public byte Y;
        public byte Z;

        public Byte8(byte s, byte t, byte u, byte v, byte w, byte x, byte y, byte z)
        {
            S = s;
            T = t;
            U = u;
            V = v;
            W = w;
            X = x;
            Y = y;
            Z = z;
        }
        public Byte8(byte[] d)
        {
            S = d[0];
            T = d[1];
            U = d[2];
            V = d[3];
            W = d[4];
            X = d[5];
            Y = d[6];
            Z = d[7];
        }
        public Byte8(float[] d, float scale)
        {
            S = FromFloat(d[0]);
            T = FromFloat(d[1]);
            U = FromFloat(d[2]);
            V = FromFloat(d[3]);
            W = FromFloat(d[4]);
            X = FromFloat(d[5]);
            Y = FromFloat(d[6]);
            Z = FromFloat(d[7]);
            byte FromFloat(float f)
            {
                float normd = f / 4 / scale;
                return (byte) (SdfMath.Saturate(normd + 0.25f) * 255);
            }
        }
        

        public byte[] Array {
            get {
                return new byte[] { S, T, U, V, W, X, Y, Z };
            }
        }
        public float[] SingleArray {
            get {
                return new float[] { S, T, U, V, W, X, Y, Z };
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            foreach (byte element in Array) {
                writer.Write(element);
            }
        }
        public static Byte8 Deserialize(BinaryReader reader)
        {
            return new Byte8(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte());
        }
    }
    class OctData
    {
        public OctS[] Structs;
        public byte[] Values;
        public int Length {
            get {
                return Structs.Length;
            }
        }
        const int MaxTextureDim = 2048;//16384;

        public OctData(OctS[] frames, Byte8[] values)
        {
            Structs = frames;
            Values = new byte[RoundToNextRow(values.Length * 4)*2];

            for (int i = 0; i < values.Length; i++) {
                Byte8 v = values[i];
                int p = ((i*4) % MaxTextureDim) + 2*MaxTextureDim * ((i*4) / MaxTextureDim);

                Values[p] =     v.S;
                Values[p + 1] = v.T;
                Values[p + 2] = v.W;
                Values[p + 3] = v.X;

                p += MaxTextureDim;
                Values[p] =     v.U;
                Values[p + 1] = v.V;
                Values[p + 2] = v.Y;
                Values[p + 3] = v.Z;
            }
            int RoundToNextRow(int x)
            {
                return (x + MaxTextureDim - 1) / MaxTextureDim * MaxTextureDim;
            }
        }
        public OctData(List<OctS> frames, List<byte> values)
        {
            Structs = frames.ToArray();
            Values = values.ToArray();
        }
        public Texture ValueTexture() {
            var desc = new TextureDescription {
                Type = TextureType.Texture2D,
                ArrayLayers = 1,
                Format = PixelFormat.R8_UNorm,
                Width = MaxTextureDim,
                Height = (uint) Values.Length / MaxTextureDim,
                Depth = 1,
                Usage = TextureUsage.Sampled,
                MipLevels = 1,
                SampleCount = TextureSampleCount.Count1
            };
            var tex = Program.factory.CreateTexture(desc);
            //desc.Usage = TextureUsage.Staging;
            //desc.SampleCount = TextureSampleCount.Count1;
            //var staging = Program.factory.CreateTexture(desc);
            uint updateWidth = MaxTextureDim;
            uint updateHeight = (uint) Values.Length / MaxTextureDim;
            Program.device.UpdateTexture(tex, Values, 0, 0, 0, updateWidth, updateHeight, 1, 0, 0);
            return tex;
        }
        public DeviceBuffer ValueBuffer()
        {
            var b = Program.MakeBuffer(Values, BufferUsage.StructuredBufferReadOnly, raw: true);
            return b;
        }
        public DeviceBuffer StructBuffer()
        {
            return Program.MakeBuffer(Structs, BufferUsage.StructuredBufferReadOnly);
        }
    }
}