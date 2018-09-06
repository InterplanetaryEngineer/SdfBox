﻿using System;
using System.IO;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using System.Runtime.InteropServices;

namespace SDFbox
{
    class Program
    {
        static Sdl2Window window;
        static GraphicsDevice graphicsDevice;
        static CommandList commandList;
        static DeviceBuffer vertexBuffer;
        static DeviceBuffer indexBuffer;
        static DeviceBuffer dataSBuffer;
        static DeviceBuffer infoSBuffer;
        static ResourceSet structuredResources;
        static ResourceLayout resourceLayout;
        static Shader vertexShader;
        static Shader fragmentShader;
        static Pipeline pipeline;
        static ResourceFactory factory;

        static void Main(string[] args)
        {
            WindowCreateInfo windowCI = new WindowCreateInfo() {
                X = 100,
                Y = 100,
                WindowWidth = 720,
                WindowHeight = 720,
                WindowTitle = "Veldrid Tutorial",
            };
            window = VeldridStartup.CreateWindow(ref windowCI);
            window.KeyDown += Logic.KeyHandler;
            window.MouseMove += (MouseMoveEventArgs mouseEvent) => {
                if (mouseEvent.State.IsButtonDown(0)) {
                    window.SetMousePosition(360, 360);
                    Logic.MouseMove(mouseEvent.MousePosition - new Vector2(360, 360));
                }
            };
            window.CursorVisible = false;

            graphicsDevice = VeldridStartup.CreateGraphicsDevice(window);

            CreateResources();

            while (window.Exists) {
                window.PumpEvents();
                Draw();
            }

            DisposeResources();
        }

        static void Draw()
        {
            window.PumpEvents();
            window.PumpEvents((ref SDL_Event ev) => {
                Console.WriteLine(ev.type);
            });
            graphicsDevice.UpdateBuffer(infoSBuffer, 0, Logic.GetInfo);
            commandList.Begin();

            commandList.SetFramebuffer(graphicsDevice.SwapchainFramebuffer);
            commandList.SetFullViewports();

            commandList.ClearColorTarget(0, RgbaFloat.Black);

            commandList.SetPipeline(pipeline);
            commandList.SetGraphicsResourceSet(0, structuredResources);
            commandList.SetVertexBuffer(0, vertexBuffer);
            commandList.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
            commandList.DrawIndexed(
                indexStart: 0,
                indexCount: 4,
                instanceStart: 0,
                instanceCount: 1,
                vertexOffset: 0);

            commandList.End();

            graphicsDevice.SubmitCommands(commandList);
            
            graphicsDevice.SwapBuffers();
        }




        static void CreateResources()
        {
            factory = graphicsDevice.ResourceFactory;
            Vertex[] quadVertices = Logic.ScreenQuads;
            ushort[] quadIndices = { 0, 1, 2, 3 };


            OctS[] octData = Logic.MakeData();

            vertexBuffer = MakeBuffer(quadVertices, BufferUsage.VertexBuffer);
            indexBuffer = MakeBuffer(quadIndices, BufferUsage.IndexBuffer);

            Console.WriteLine(Marshal.SizeOf(octData[0]));

            dataSBuffer = MakeBuffer(octData, BufferUsage.StructuredBufferReadOnly);
            infoSBuffer = MakeBuffer(Logic.GetInfo, BufferUsage.StructuredBufferReadOnly);
            resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(new ResourceLayoutElementDescription[] {
                new ResourceLayoutElementDescription("SB0", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SB1", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment)
            }));
            structuredResources = factory.CreateResourceSet(new ResourceSetDescription(resourceLayout, dataSBuffer, infoSBuffer));

            /* ===
            dataUBuffer = MakeBuffer(octData, BufferUsage.UniformBuffer);//octData, BufferUsage.UniformBuffer);
            infoUBuffer = MakeBuffer(Logic.GetInfo, BufferUsage.UniformBuffer);
            resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription( new ResourceLayoutElementDescription[] {
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("UB1", ResourceKind.UniformBuffer, ShaderStages.Fragment)
            }));
            uniformResources = factory.CreateResourceSet(new ResourceSetDescription(resourceLayout, dataUBuffer, infoUBuffer));
            // === */

            vertexShader = LoadShader(ShaderStages.Vertex);
            fragmentShader = LoadShader(ShaderStages.Fragment);

            MakePipeline();

            commandList = factory.CreateCommandList();
        }

        static DeviceBuffer MakeBuffer<T>(T[] data, BufferUsage usage, uint size = 0) where T : struct
        {
            BufferDescription description;
            uint structuredStride = 0;
            if (usage == BufferUsage.StructuredBufferReadOnly || usage == BufferUsage.StructuredBufferReadWrite) {
                structuredStride = Size();
            }
            if (size == 0) {
                description = new BufferDescription(Size(), usage, structuredStride);
            } else {
                description = new BufferDescription(size, usage, structuredStride);
            }
            DeviceBuffer newBuffer = factory.CreateBuffer(description);
            graphicsDevice.UpdateBuffer(newBuffer, 0, data);
            return newBuffer;

            uint Size()
            {
                Console.WriteLine(data.Length);
                int singleSize = Marshal.SizeOf(data[0]);
                return (uint) (data.Length * singleSize);
            }
        }

        static Shader LoadShader(ShaderStages stage)
        {
            string extension = GraphicsExtension();
            Console.WriteLine(extension);
            string entryPoint = (stage == ShaderStages.Vertex ? "VS" : "FS");
            string path = Path.Combine(AppContext.BaseDirectory, "Shaders", $"{stage.ToString()}.{extension}");
            byte[] shaderBytes = File.ReadAllBytes(path);
            return graphicsDevice.ResourceFactory.CreateShader(new ShaderDescription(stage, shaderBytes, entryPoint));
        }

        static string GraphicsExtension()
        {
            switch (graphicsDevice.BackendType) {
                case GraphicsBackend.Direct3D11:
                    return ("hlsl.bytes");
                case GraphicsBackend.Vulkan:
                    return ("spv");
                case GraphicsBackend.OpenGL:
                    return ("glsl");
                case GraphicsBackend.Metal:
                    return ("metallib");
                default: throw new InvalidOperationException();
            }
        }



        static void MakePipeline()
        {
            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription {
                BlendState = BlendStateDescription.SingleOverrideBlend
            };

            SetStencilState(pipelineDescription);
            SetRasterizerState(pipelineDescription);

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            pipelineDescription.ResourceLayouts = new ResourceLayout[] { resourceLayout };
            pipelineDescription.ShaderSet = MakeShaderSet(LayoutDescription(), vertexShader, fragmentShader);
            pipelineDescription.Outputs = graphicsDevice.SwapchainFramebuffer.OutputDescription;

            pipeline = factory.CreateGraphicsPipeline(pipelineDescription);
        }

        static VertexLayoutDescription LayoutDescription()
        {
            return (new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)));
        }

        static void SetStencilState(GraphicsPipelineDescription pipelineDescription,
            bool depthTest = true, bool depthWrite = true,
            ComparisonKind comparison = ComparisonKind.LessEqual)
        {
            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: depthTest,
                depthWriteEnabled: depthWrite,
                comparisonKind: comparison);
        }

        static void SetRasterizerState(GraphicsPipelineDescription pipelineDescription,
            FaceCullMode faceCull = FaceCullMode.Back,
            PolygonFillMode polygonFill = PolygonFillMode.Solid,
            FrontFace front = FrontFace.Clockwise,
            bool depthClip = true, bool scissorTest = false)
        {
            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: faceCull,
                fillMode: polygonFill,
                frontFace: front,
                depthClipEnabled: depthClip,
                scissorTestEnabled: scissorTest);
        }

        static ShaderSetDescription MakeShaderSet(VertexLayoutDescription vertexLayout, Shader vertexShader, Shader fragmentShader)
        {
            return (new ShaderSetDescription(
                new VertexLayoutDescription[] { vertexLayout },
                new Shader[] { vertexShader, fragmentShader }));
        }
        
        static void DisposeResources()
        {
            pipeline.Dispose();
            vertexShader.Dispose();
            fragmentShader.Dispose();
            commandList.Dispose();
            vertexBuffer.Dispose();
            indexBuffer.Dispose();
            graphicsDevice.Dispose();
        }


        public static Vector2 ScreenSize {
            get {
                return new Vector2(window.Width, window.Height);
            }
        }
    }

    struct Vertex
    {
        public Vector2 Position; // This is the position, in normalized device coordinates.
        public RgbaFloat Color; // This is the color of the vertex.
        public Vertex(Vector2 position, RgbaFloat color)
        {
            Position = position;
            Color = color;
        }
        public Vertex(float x, float y, RgbaFloat color)
        {
            Position = new Vector2(x, y);
            Color = color;
        }
    }

    ///*
    [StructLayout(LayoutKind.Sequential, Pack = 128, Size = 128)]
    struct OctS
    {
        public Int32 Parent;
        public Vector3 lower;
        public Vector3 higher;
        public float b;

        public Int8 children;
        public Vector8 verts;
        public Int32 empty;
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
        public OctS(int Parent, int[] children, float[] verts, Vector3 lower, Vector3 higher)
        {
            this.Parent = Parent;
            this.lower = lower;
            this.higher = higher;

            this.children = new Int8(children);
            this.verts = new Vector8(verts);

            empty = 1;
            foreach(float x in verts) {
                if (x <= 0)
                    empty = 0;
            }

            b = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16, Size = 16)]
    struct Int4
    {
        public Int32 X;
        public Int32 Y;
        public Int32 Z;
        public Int32 W;

        public Int4(int x, int y, int z, int w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16, Size = 32)]
    struct Int8
    {
        public Int32 S;
        public Int32 T;
        public Int32 U;
        public Int32 V;
        public Int32 W;
        public Int32 X;
        public Int32 Y;
        public Int32 Z;

        public Int8(int s, int t, int u, int v, int w, int x, int y, int z)
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
        public Int8(int[] d)
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
    }
}