﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EngineTest.Entities;
using EngineTest.Entities.Editor;
using EngineTest.Main;
using EngineTest.Recources;
using EngineTest.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EngineTest.Renderer.RenderModules
{
    public class EditorRender
    {
        public IdRenderer _idRenderer;
        public GraphicsDevice _graphicsDevice;

        private BillboardBuffer _billboardBuffer;

        private Assets _assets;

        private double mouseMoved;
        private bool mouseMovement = false;

        public void Initialize(GraphicsDevice graphics, Assets assets)
        {
            _graphicsDevice = graphics;
            _assets = assets;

            _billboardBuffer = new BillboardBuffer(Color.White, graphics);
            _idRenderer = new IdRenderer();
            _idRenderer.Initialize(graphics, _billboardBuffer, _assets);

        }

        public void Update(GameTime gameTime)
        {
            if (Input.mouseState != Input.mouseLastState)
            {
                //reset the timer!

                mouseMoved = gameTime.TotalGameTime.TotalMilliseconds + 500;
                mouseMovement = true;
            }

            if (mouseMoved < gameTime.TotalGameTime.TotalMilliseconds)
            {
                mouseMovement = false;
            }

        }

        public void SetUpRenderTarget(int width, int height)
        {
            _idRenderer.SetUpRenderTarget(width, height);
        }

        public void DrawBillboards(List<PointLight> lights, Matrix staticViewProjection, EditorLogic.EditorSendData sendData)
        {
            int hoveredId = GetHoveredId();

            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            _graphicsDevice.SetVertexBuffer(_billboardBuffer.VBuffer);
            _graphicsDevice.Indices = (_billboardBuffer.IBuffer);

            Shaders.BillboardEffectParameter_Texture.SetValue(_assets.Icon_Light);

            Shaders.BillboardEffect.CurrentTechnique = Shaders.BillboardEffectTechnique_Billboard;

            Shaders.BillboardEffectParameter_IdColor.SetValue(Color.Gray.ToVector3());

            foreach (var light in lights)
            {
                Matrix world = Matrix.CreateTranslation(light.Position);
                Shaders.BillboardEffectParameter_WorldViewProj.SetValue(world*staticViewProjection);

                if (light.Id == GetHoveredId()) Shaders.BillboardEffectParameter_IdColor.SetValue(Color.White.ToVector3());
                if (light.Id == sendData.SelectedObjectId) 
                    Shaders.BillboardEffectParameter_IdColor.SetValue(Color.Gold.ToVector3());

                Shaders.BillboardEffect.CurrentTechnique.Passes[0].Apply();

                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);

                if (light.Id == GetHoveredId() || light.Id == sendData.SelectedObjectId) Shaders.BillboardEffectParameter_IdColor.SetValue(Color.Gray.ToVector3());
            }
        }

        public void DrawIds(MeshMaterialLibrary meshMaterialLibrary, List<PointLight>lights, Matrix staticViewProjection, EditorLogic.EditorSendData editorData)
        {
            _idRenderer.Draw(meshMaterialLibrary, lights, staticViewProjection, editorData, mouseMovement);
        }

        public void DrawEditorElements(MeshMaterialLibrary meshMaterialLibrary, List<PointLight> lights, Matrix staticViewProjection, EditorLogic.EditorSendData editorData)
        {
            _graphicsDevice.SetRenderTarget(null);
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.BlendState = BlendState.Opaque;

            DrawGizmo(staticViewProjection, editorData);
            DrawBillboards(lights, staticViewProjection, editorData);
        }

        public void DrawGizmo(Matrix staticViewProjection, EditorLogic.EditorSendData editorData)
        {
            if (editorData.SelectedObjectId == 0) return;

            

            Vector3 position = editorData.SelectedObjectPosition;
            EditorLogic.GizmoModes gizmoMode = editorData.GizmoMode;

            //Z
            DrawArrow(position, Math.PI, 0, 0, GetHoveredId() == 1 ? 1 : 0.5f, Color.Blue, staticViewProjection, gizmoMode); //z 1
            DrawArrow(position, -Math.PI / 2, 0, 0, GetHoveredId() == 2 ? 1 : 0.5f, Color.Green, staticViewProjection, gizmoMode); //y 2
            DrawArrow(position, 0, Math.PI / 2, 0, GetHoveredId() == 3 ? 1 : 0.5f, Color.Red, staticViewProjection, gizmoMode); //x 3

            //DrawArrowRound(position, Math.PI, 0, 0, GetHoveredId() == 1 ? 1 : 0.5f, Color.Blue, staticViewProjection); //z 1
            //DrawArrowRound(position, -Math.PI / 2, 0, 0, GetHoveredId() == 2 ? 1 : 0.5f, Color.Green, staticViewProjection); //y 2
            //DrawArrowRound(position, 0, Math.PI / 2, 0, GetHoveredId() == 3 ? 1 : 0.5f, Color.Red, staticViewProjection); //x 3
        }

        private void DrawArrow(Vector3 Position, double AngleX, double AngleY, double AngleZ, float Scale, Color color, Matrix staticViewProjection, EditorLogic.GizmoModes gizmoMode)
        {
            Matrix Rotation = Matrix.CreateRotationX((float)AngleX) * Matrix.CreateRotationY((float)AngleY) *
                               Matrix.CreateRotationZ((float)AngleZ);
            Matrix ScaleMatrix = Matrix.CreateScale(0.75f, 0.75f,Scale*1.5f);
            Matrix WorldViewProj = ScaleMatrix * Rotation * Matrix.CreateTranslation(Position) * staticViewProjection;

            Shaders.IdRenderEffectParameterWorldViewProj.SetValue(WorldViewProj);
            Shaders.IdRenderEffectParameterColorId.SetValue(color.ToVector4());

            Model model = gizmoMode == EditorLogic.GizmoModes.translation
                ? _assets.EditorArrow
                : _assets.EditorArrowRound;

            foreach (ModelMesh mesh in model.Meshes)
            {
                foreach (ModelMeshPart meshpart in mesh.MeshParts)
                {
                    Shaders.IdRenderEffectDrawId.Apply();

                    _graphicsDevice.SetVertexBuffer(meshpart.VertexBuffer);
                    _graphicsDevice.Indices = (meshpart.IndexBuffer);
                    int primitiveCount = meshpart.PrimitiveCount;
                    int vertexOffset = meshpart.VertexOffset;
                    int vCount = meshpart.NumVertices;
                    int startIndex = meshpart.StartIndex;

                    _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, vertexOffset, startIndex, primitiveCount);
                }
            }
        }


        public RenderTarget2D GetOutlines()
        {
            return _idRenderer.GetRT();
        }

        /// <summary>
        /// Returns the id of the currently hovered object
        /// </summary>
        /// <returns></returns>
        public int GetHoveredId()
        {
            return _idRenderer.HoveredId;
        }
    }
}
