﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using EngineTest.Entities;
using EngineTest.Main;
using EngineTest.Recources;
using EngineTest.Renderer.Helper;
using EngineTest.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DirectionalLight = EngineTest.Recources.DirectionalLight;

namespace EngineTest.Renderer
{
    public class Renderer
    {
        ///////////////////////////////////////////////////// FIELDS ////////////////////////////////////


        //Graphics
        private GraphicsDevice _graphicsDevice;
        private SpriteBatch _spriteBatch;
        private QuadRenderer _quadRenderer;
        private GaussianBlur _gaussianBlur;
        private EditorRender _editorRender;

        //Checkvariables for change
        private float _supersampling = 1;
        private bool _hologramDraw;
        private int _forceShadowFiltering = 0;
        private bool _forceShadowSS = false;

        private Assets _assets;

        //View Projection
        private bool viewProjectionHasChanged;

        private bool _temporalAAOffFrame = true;
        private int _temporalAAFrame = 0;

        private Matrix _currentToPrevious;

        private Matrix _view;
        private Matrix _projection;
        private Matrix _viewProjection;
        private Matrix _staticViewProjection;
        private Matrix _inverseViewProjection;

        private Matrix _previousViewProjection;

        private BoundingFrustum _boundingFrustum;
        private BoundingFrustum _boundingFrustumShadow;

        //RenderTargets
        public enum RenderModes { Albedo, Normal, Depth, Deferred, Diffuse, Specular, Hologram,
            SSAO,
            Emissive,
            DirectionalShadow
        };

        private RenderTarget2D _renderTargetAlbedo;
        private RenderTargetBinding[] _renderTargetBinding = new RenderTargetBinding[3];

        private RenderTarget2D _renderTargetDepth;
        private RenderTarget2D _renderTargetNormal;
        private RenderTarget2D _renderTargetDiffuse;
        private RenderTarget2D _renderTargetSpecular;
        private RenderTargetBinding[] _renderTargetLightBinding = new RenderTargetBinding[2];

        private RenderTarget2D _renderTargetFinal;
        private RenderTargetBinding[] _renderTargetFinalBinding = new RenderTargetBinding[1];

        //TAA
        private RenderTarget2D _renderTargetFinal2;
        private RenderTargetBinding[] _renderTargetFinal2Binding = new RenderTargetBinding[1];

        private RenderTarget2D _renderTargetScreenSpaceEffect2;

        private RenderTarget2D _renderTargetHologram;

        private RenderTarget2D _renderTargetScreenSpaceEffect;

        private RenderTarget2D _renderTargetScreenSpaceEffectPrepareBlur;

        private RenderTarget2D _renderTargetScreenSpaceEffectBlurX;
        private RenderTarget2D _renderTargetScreenSpaceEffectBlurYFinal;

        private RenderTarget2D _renderTargetEmissive;

        //Cubemap
        private RenderTargetCube _renderTargetCubeMap;

        //BlendStates
        
        private BlendState _lightBlendState;
        private BlendState _linearBlendState;
        /////////////////////////////////////////////////////// FUNCTIONS ////////////////////////////////

        #region initialize
        //Done after Load
        public void Initialize(GraphicsDevice graphicsDevice, Assets assets)
        {
            _graphicsDevice = graphicsDevice;
            _quadRenderer = new QuadRenderer();
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _gaussianBlur = new GaussianBlur();
            _gaussianBlur.Initialize(graphicsDevice);

            _editorRender = new EditorRender();
            _editorRender.Initialize(graphicsDevice, assets);

            //SetUpRenderTargets(_graphicsDevice.PresentationParameters.BackBufferWidth, _graphicsDevice.PresentationParameters.BackBufferHeight);

            _assets = assets;

            GameSettings.ApplySSAO();

            SetUpRenderTargets(GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight, false);
        }

        //Update per frame
        public void Update(GameTime gameTime, bool isActive)
        {
            if (!isActive) return;
            _editorRender.Update(gameTime);
        }

        //Load content
        public void Load(ContentManager content)
        {
            _lightBlendState = new BlendState
            {
                AlphaSourceBlend = Blend.One,
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One,
                AlphaDestinationBlend = Blend.One
            };

            _linearBlendState = new BlendState
            {
                AlphaSourceBlend = Blend.BlendFactor,
                ColorSourceBlend = Blend.BlendFactor,
                ColorDestinationBlend = Blend.BlendFactor,
                AlphaDestinationBlend = Blend.BlendFactor,
                BlendFactor = new Color(0.5f, 0.5f, 0.5f, 0.5f)
            };
        }
#endregion

        
        //Main Draw!
        public EditorLogic.EditorReceivedData Draw(Camera camera, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities, List<PointLight> pointLights, List<DirectionalLight> dirLights, EditorLogic.EditorSendData editorData, GameTime gameTime)
        {
            //Reset the stat counter
            ResetStats(); 

            //Check if we changed some drastic stuff for which we need to reload some elements
            CheckRenderChanges(dirLights);

            //Render ShadowMaps
            DrawShadows(meshMaterialLibrary, entities, pointLights, dirLights, camera);

            //Render EnvironmentMaps
            if ((Input.WasKeyPressed(Keys.C)&&!DebugScreen.ConsoleOpen) || GameSettings.g_EnvironmentMappingEveryFrame || _renderTargetCubeMap == null)
            {
                DrawCubeMap(camera.Position, meshMaterialLibrary, entities, pointLights, dirLights, 300);
                camera.HasChanged = true;
            }

            //Update our view projection matrices if the camera moved
            UpdateViewProjection(camera, meshMaterialLibrary, entities);

            //Set up our deferred renderer
            SetUpGBuffer();

            DrawGBuffer(meshMaterialLibrary, entities);

            DrawHolograms(meshMaterialLibrary);


            //Custom Effect
            DrawScreenSpaceEffect(camera);

            DrawScreenSpaceDirectionalShadow(dirLights);

            DrawBilateralBlur();

            //Light the scene
            DrawLights(pointLights, dirLights, camera.Position);

            DrawEnvironmentMap(camera.Position);

            DrawEmissiveEffect(entities, camera, meshMaterialLibrary, gameTime);


            //Combine the buffers
            Compose();

            //DrawScreenSpaceEffect2(camera);

            CombineTemporalAntialiasing();
                
            if(GameSettings.Editor_enable) _editorRender.DrawIds(meshMaterialLibrary, pointLights, _staticViewProjection, editorData);
            //Show certain buffer stages depending on user input
            RenderMode();

            if (GameSettings.Editor_enable)
            {
                DrawMapToScreenToFullScreen(_editorRender.GetOutlines(), BlendState.Additive);

                _editorRender.DrawEditorElements(meshMaterialLibrary, pointLights, _staticViewProjection, editorData);

            }



            //Just some object culling, setting up for the next frame
            meshMaterialLibrary.FrustumCullingFinalizeFrame(entities);

            return new EditorLogic.EditorReceivedData
            {
                HoveredId =  _editorRender.GetHoveredId(),
                ViewMatrix =  _view,
                ProjectionMatrix =  _projection
            };
        }


        private void CombineTemporalAntialiasing()
        {
             //if offframe we have drawn to rendertarget2

            if (!GameSettings.g_TemporalAntiAliasing) return;

            
            //NAIVE
            /*
            _spriteBatch.Begin(SpriteSortMode.Deferred, _linearBlendState);
            _spriteBatch.Draw(_temporalAAOffFrame ? _renderTargetFinal : _renderTargetFinal2, new Rectangle(0, 0, (int) (GameSettings.g_ScreenWidth * GameSettings.g_supersampling), (int) (GameSettings.g_ScreenHeight * GameSettings.g_supersampling)), Color.White);
            _spriteBatch.End(); 
            */

            _graphicsDevice.BlendState = _linearBlendState;

            Shaders.TemporalAntiAliasingEffect_AccumulationMap.SetValue(_temporalAAOffFrame ? _renderTargetFinal : _renderTargetFinal2);
            Shaders.TemporalAntiAliasingEffect_CurrentToPrevious.SetValue(_currentToPrevious);

            Shaders.TemporalAntiAliasingEffect.CurrentTechnique.Passes[0].Apply();
            _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);

            _graphicsDevice.BlendState = BlendState.Opaque;
        }


        private void DrawCubeMap(Vector3 origin, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities,
            List<PointLight> pointLights, List<DirectionalLight> dirLights, float farPlane )
        {
            if (_renderTargetCubeMap == null) // _renderTargetCubeMap.Dispose();
            {
                _renderTargetCubeMap = new RenderTargetCube(_graphicsDevice, 512, true, SurfaceFormat.Color,
                    DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);

                Shaders.deferredEnvironment.Parameters["ReflectionCubeMap"].SetValue(_renderTargetCubeMap);
            }
            SetUpRenderTargets(512, 512, true);

            Shaders.DeferredCompose.Parameters["useSSAO"].SetValue(false);

            _projection = Matrix.CreatePerspectiveFieldOfView((float) (Math.PI/2), 1, 1, farPlane);

            for (int i = 0; i < 6; i++)
            {
                // render the scene to all cubemap faces
                CubeMapFace cubeMapFace = (CubeMapFace) i;

                switch (cubeMapFace)
                {
                    case CubeMapFace.NegativeX:
                    {
                        _view = Matrix.CreateLookAt(origin, origin + Vector3.Left, Vector3.Up);
                        break;
                    }
                    case CubeMapFace.NegativeY:
                    {
                        _view = Matrix.CreateLookAt(origin, origin + Vector3.Down, Vector3.Forward);
                        break;
                    }
                    case CubeMapFace.NegativeZ:
                    {
                        _view = Matrix.CreateLookAt(origin, origin + Vector3.Backward, Vector3.Up);
                        break;
                    }
                    case CubeMapFace.PositiveX:
                    {
                        _view = Matrix.CreateLookAt(origin, origin + Vector3.Right, Vector3.Up);
                        break;
                    }
                    case CubeMapFace.PositiveY:
                    {
                        _view = Matrix.CreateLookAt(origin, origin + Vector3.Up, Vector3.Backward);
                        break;
                    }
                    case CubeMapFace.PositiveZ:
                    {
                        _view = Matrix.CreateLookAt(origin, origin + Vector3.Forward, Vector3.Up);
                        break;
                    }
                }

                _viewProjection = _view*_projection;
                _inverseViewProjection = Matrix.Invert(_viewProjection);

                viewProjectionHasChanged = true;

                if (_boundingFrustum != null)
                    _boundingFrustum.Matrix = _viewProjection;
                else
                    _boundingFrustum = new BoundingFrustum(_viewProjection);
                //cull
                meshMaterialLibrary.FrustumCulling(entities, _boundingFrustum, true, origin);

                SetUpGBuffer();

                DrawGBuffer(meshMaterialLibrary, entities);

                DrawScreenSpaceDirectionalShadow(dirLights);

                DrawLights(pointLights, dirLights, origin);

                DrawEnvironmentMap(origin);

                bool tempAA = GameSettings.g_TemporalAntiAliasing;

                GameSettings.g_TemporalAntiAliasing = false;

                Compose();

                GameSettings.g_TemporalAntiAliasing = tempAA;

                DrawMapToScreenToCube(_renderTargetFinal, _renderTargetCubeMap, cubeMapFace);

            }

            Shaders.DeferredCompose.Parameters["useSSAO"].SetValue(GameSettings.ssao_Active);

            SetUpRenderTargets(GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight, false);

        }

        private void RenderMode()
        {
            switch (GameSettings.g_RenderMode)
            {
               case RenderModes.Albedo:
                    DrawMapToScreenToFullScreen(_renderTargetAlbedo);
                    break;
               case RenderModes.Normal:
                    DrawMapToScreenToFullScreen(_renderTargetNormal);
                    break;
               case RenderModes.Depth:
                    DrawMapToScreenToFullScreen(_renderTargetDepth);
                    break;
               case RenderModes.Diffuse:
                    DrawMapToScreenToFullScreen(_renderTargetDiffuse);
                    break;
               case RenderModes.Specular:
                    DrawMapToScreenToFullScreen(_renderTargetSpecular);
                    break;
                case RenderModes.SSAO:
                    DrawMapToScreenToFullScreen(_renderTargetScreenSpaceEffect);
                    break;
                case RenderModes.Hologram:
                    DrawMapToScreenToFullScreen(_renderTargetHologram);
                    break;
                case RenderModes.DirectionalShadow:
                    DrawMapToScreenToFullScreen(_renderTargetScreenSpaceEffectPrepareBlur);
                    break;
                case RenderModes.Emissive:
                    DrawMapToScreenToFullScreen(_renderTargetEmissive);
                    break;
                default:
                    if (GameSettings.g_TemporalAntiAliasing)
                    {
                        DrawMapToScreenToFullScreen(_temporalAAOffFrame ? _renderTargetFinal2 : _renderTargetFinal);
                    }
                    else
                    {
                        DrawMapToScreenToFullScreen(_renderTargetFinal);
                    }
                    break;
            }
        }

        #region ScreenSpaceEffect

        private void DrawBilateralBlur()
        {
            _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectPrepareBlur);

            _spriteBatch.Begin(0,BlendState.Additive);

            _spriteBatch.Draw(_renderTargetScreenSpaceEffect, new Rectangle(0,0,GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight), Color.Red);

            _spriteBatch.End();


            if (GameSettings.ssao_Blur)
            {
                _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

                _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectBlurX);

                Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetScreenSpaceEffectPrepareBlur);
                Shaders.ScreenSpaceEffectTechnique_BlurVertical.Passes[0].Apply();

                _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One*-1, Vector2.One);

                _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectBlurYFinal);

                Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetScreenSpaceEffectBlurX);
                Shaders.ScreenSpaceEffectTechnique_BlurHorizontal.Passes[0].Apply();

                _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One*-1, Vector2.One);
            }
            else
            {
                _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectBlurYFinal);

                _spriteBatch.Begin(0, BlendState.Opaque);

                _spriteBatch.Draw(_renderTargetScreenSpaceEffectPrepareBlur, new Rectangle(0, 0, GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight), Color.White);

                _spriteBatch.End();
            }

        }

        private void DrawScreenSpaceEffect(Camera camera)
        {
            if (!GameSettings.ssao_Active) return;

            _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffect);

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            Shaders.ScreenSpaceEffectParameter_InverseViewProjection.SetValue(_inverseViewProjection);
            Shaders.ScreenSpaceEffectParameter_Projection.SetValue(_projection);
            Shaders.ScreenSpaceEffectParameter_ViewProjection.SetValue(_viewProjection);
            Shaders.ScreenSpaceEffectParameter_CameraPosition.SetValue(camera.Position);

            Shaders.ScreenSpaceEffect.CurrentTechnique = Shaders.ScreenSpaceEffectTechnique_SSAO;
            Shaders.ScreenSpaceEffect.CurrentTechnique.Passes[0].Apply();
            _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);

            //BLUR

            //need bilateral upsample
        }

        private void DrawScreenSpaceEffect2(Camera camera)
        {
            //Another way to make SSR, not good yet

            _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffect2);

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            
            Shaders.ScreenSpaceEffect2Parameter_InverseViewProjection.SetValue(_inverseViewProjection);
            //Shaders.ScreenSpaceEffect2Parameter_Projection.SetValue(_projection);
            Shaders.ScreenSpaceEffect2Parameter_ViewProjection.SetValue(_viewProjection);
            //Shaders.ScreenSpaceEffectParameter_CameraPosition.SetValue(camera.Position);

            //Shaders.ScreenSpaceEffect.CurrentTechnique = Shaders.ScreenSpaceEffectTechnique_SSAO;
            Shaders.ScreenSpaceEffect2.CurrentTechnique.Passes[0].Apply();
            _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);

            DrawMapToScreenToFullScreen(_renderTargetScreenSpaceEffect2);


        }

        private void DrawEmissiveEffect(List<BasicEntity> entities, Camera camera, MeshMaterialLibrary meshMatLib, GameTime gameTime)
        {
            if (!GameSettings.g_EmissiveDraw) return;

            //Make a new _viewProjection

            //This should actually scale dynamically with the position of the object
            //Note: It would be better if the screen extended the same distance in each direction, right now it would probably be wider than tall
            Matrix newProjection = Matrix.CreatePerspectiveFieldOfView(Math.Min((float)Math.PI, camera.FieldOfView*GameSettings.g_EmissiveDrawFOVFactor),
                    GameSettings.g_ScreenWidth / (float)GameSettings.g_ScreenHeight, 1, GameSettings.g_FarPlane);

            Matrix transformedViewProjection = _view*newProjection;

            meshMatLib.DrawEmissive(_graphicsDevice, camera, _viewProjection, transformedViewProjection, _inverseViewProjection, _renderTargetEmissive, _renderTargetDiffuse, _renderTargetSpecular, _lightBlendState, _assets.Sphere.Meshes, gameTime);

            //blur
            /*
            if (renderedonce && false)
            {
                _graphicsDevice.BlendState = BlendState.Opaque;
                _graphicsDevice.DepthStencilState = DepthStencilState.Default;
                _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

                _graphicsDevice.SetRenderTarget(_renderTargetHologram);

                Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetDiffuse);
                Shaders.ScreenSpaceEffect.CurrentTechnique = Shaders.ScreenSpaceEffectTechnique_BlurVertical;
                Shaders.ScreenSpaceEffect.CurrentTechnique.Passes[0].Apply();

                _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);

                _graphicsDevice.SetRenderTarget(_renderTargetDiffuse);

                Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetHologram);
                Shaders.ScreenSpaceEffect.CurrentTechnique = Shaders.ScreenSpaceEffectTechnique_BlurHorizontal;
                Shaders.ScreenSpaceEffect.CurrentTechnique.Passes[0].Apply();

                _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);
            }

            //_graphicsDevice.SetRenderTarget(null);
            //_spriteBatch.Begin(0, BlendState.Additive, SamplerState.AnisotropicClamp);
            //_spriteBatch.Draw(_renderTargetFinal, new Rectangle(0, 0, GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight), Color.White);
            //_spriteBatch.End();

            //DrawMapToScreenToFullScreen(_renderTargetHologram);
             * */
        }

        #endregion

        #region Shadow Mapping

        //todo: don't draw shadows for lights that are not visible
        private void DrawShadows(MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities, List<PointLight> pointLights, List<DirectionalLight> dirLights, Camera camera)
        {
            //Don't render for the first frame, we need a guideline first
            if (_boundingFrustum == null) UpdateViewProjection(camera, meshMaterialLibrary, entities);

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;

            foreach (PointLight light in pointLights)
            {
                if (_boundingFrustum.Contains(light.BoundingSphere) == ContainmentType.Disjoint)
                {
                    continue;
                }

                if (light.DrawShadow)
                {
                    GameStats.shadowMaps += 6;

                    //Check for static
                    if (!light.StaticShadows || light.shadowMapCube == null)
                    {
                        CreateCubeShadowMap(light, light.ShadowResolution, meshMaterialLibrary, entities);
                        camera.HasChanged = true;
                        light.HasChanged = false;
                    }
                }
            }

            int dirLightShadowed = 0;
            foreach (DirectionalLight light in dirLights)
            {
                if (light.DrawShadows)
                {
                    GameStats.shadowMaps += 1;

                    CreateShadowMap(light, light.ShadowResolution, meshMaterialLibrary, entities);

                    camera.HasChanged = true;
                    light.HasChanged = false;
                    dirLightShadowed++;
                }

                if (dirLightShadowed > 1)
                {
                    throw new NotImplementedException("Only one shadowed DirectionalLight is supported right now");
                }
            }

        }

        private void DrawScreenSpaceDirectionalShadow(List<DirectionalLight> dirLights)
        { 
            if (viewProjectionHasChanged)
            {
                Shaders.deferredDirectionalLightParameterViewProjection.SetValue(_viewProjection);
                Shaders.deferredDirectionalLightParameterInverseViewProjection.SetValue(_inverseViewProjection);
            }
            foreach (DirectionalLight light in dirLights)
            {
                if (light.DrawShadows && light.ScreenSpaceShadowBlur)
                {
                    //Draw our map!
                    _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectPrepareBlur);

                    Shaders.deferredDirectionalLightParameter_LightDirection.SetValue(light.Direction);

                    Shaders.deferredDirectionalLightParameterLightViewProjection.SetValue(light.LightViewProjection);
                    Shaders.deferredDirectionalLightParameter_ShadowMap.SetValue(light.ShadowMap);
                    Shaders.deferredDirectionalLightParameter_ShadowFiltering.SetValue((int)light.ShadowFiltering);
                    Shaders.deferredDirectionalLightParameter_ShadowMapSize.SetValue((float)light.ShadowResolution);

                    Shaders.deferredDirectionalLightShadowOnly.Passes[0].Apply();   

                    _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);
                }
            }
        }

        //Create the shadowMap for our directional Light
        private void CreateShadowMap(DirectionalLight light, int shadowResolution, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities)
        {
            //Create a renderTarget if we don't have one yet
            if (light.ShadowMap == null)
            {
                if (light.ShadowFiltering != DirectionalLight.ShadowFilteringTypes.VSM)
                {
                    light.ShadowMap = new RenderTarget2D(_graphicsDevice, shadowResolution, shadowResolution, false,
                        SurfaceFormat.HalfSingle, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
                }
                else //For a VSM shadowMap we need 2 components
                {
                    light.ShadowMap = new RenderTarget2D(_graphicsDevice, shadowResolution, shadowResolution, false,
                       SurfaceFormat.Vector2, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
                }
            }

            MeshMaterialLibrary.RenderType renderType = light.ShadowFiltering == DirectionalLight.ShadowFilteringTypes.VSM
                ? MeshMaterialLibrary.RenderType.shadowVSM
                : MeshMaterialLibrary.RenderType.shadowDepth;

            if (light.HasChanged)
            {
                Matrix LightProjection = Matrix.CreateOrthographic(light.ShadowSize, light.ShadowSize,
                    -light.ShadowDepth, light.ShadowDepth);
                Matrix LightView = Matrix.CreateLookAt(light.Position, light.Position + light.Direction, Vector3.Down);

                light.LightViewProjection = LightView*LightProjection;

                _boundingFrustumShadow = new BoundingFrustum(light.LightViewProjection);

                _graphicsDevice.SetRenderTarget(light.ShadowMap);
                _graphicsDevice.Clear(ClearOptions.DepthBuffer, Color.White, 1, 0);

                meshMaterialLibrary.FrustumCulling(entities, _boundingFrustumShadow, true, light.Position);

                // Rendering!

                meshMaterialLibrary.Draw(renderType, _graphicsDevice,
                    light.LightViewProjection, light.HasChanged, false);
            }
            else
            {
                _boundingFrustumShadow = new BoundingFrustum(light.LightViewProjection);

                bool hasAnyObjectMoved = meshMaterialLibrary.FrustumCulling(entities: entities, boundingFrustrum: _boundingFrustumShadow, hasCameraChanged: false, cameraPosition: light.Position);

                if (!hasAnyObjectMoved) return;

                meshMaterialLibrary.FrustumCulling(entities: entities, boundingFrustrum: _boundingFrustumShadow, hasCameraChanged: true, cameraPosition: light.Position);

                _graphicsDevice.SetRenderTarget(light.ShadowMap);
                _graphicsDevice.Clear(ClearOptions.DepthBuffer, Color.White, 1, 0);

                meshMaterialLibrary.Draw(renderType, _graphicsDevice,
                    light.LightViewProjection, false, true);
            }

            //Blur!
            if (light.ShadowFiltering == DirectionalLight.ShadowFilteringTypes.VSM)
            {
                light.ShadowMap = _gaussianBlur.DrawGaussianBlur(light.ShadowMap);
            }

        }

        private void CreateCubeShadowMap(PointLight light, int size, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities )
        {
            if (light.shadowMapCube == null)
                light.shadowMapCube = new RenderTargetCube(_graphicsDevice, size, false, SurfaceFormat.Vector2, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);

            Matrix LightViewProjection = new Matrix();
            CubeMapFace cubeMapFace = CubeMapFace.NegativeX;

            if (light.HasChanged)
            {
                Matrix LightProjection = Matrix.CreatePerspectiveFieldOfView((float) (Math.PI/2), 1, 1, light.Radius);
                Matrix LightView = Matrix.Identity;

                for (int i = 0; i < 6; i++)
                {
                    // render the scene to all cubemap faces
                    cubeMapFace = (CubeMapFace) i;

                    switch (cubeMapFace)
                    {
                        case CubeMapFace.NegativeX:
                        {
                            LightView = Matrix.CreateLookAt(light.Position, light.Position + Vector3.Left, Vector3.Up);

                            LightViewProjection = LightView*LightProjection;

                            light.LightViewProjectionNegativeX = LightViewProjection;
                            break;
                        }
                        case CubeMapFace.NegativeY:
                        {
                            LightView = Matrix.CreateLookAt(light.Position, light.Position + Vector3.Down,
                                Vector3.Forward);

                            LightViewProjection = LightView*LightProjection;

                            light.LightViewProjectionNegativeY = LightViewProjection;
                            break;
                        }
                        case CubeMapFace.NegativeZ:
                        {
                            LightView = Matrix.CreateLookAt(light.Position, light.Position + Vector3.Backward,
                                Vector3.Up);

                            LightViewProjection = LightView*LightProjection;

                            light.LightViewProjectionNegativeZ = LightViewProjection;
                            break;
                        }
                        case CubeMapFace.PositiveX:
                        {
                            LightView = Matrix.CreateLookAt(light.Position, light.Position + Vector3.Right, Vector3.Up);

                            LightViewProjection = LightView*LightProjection;

                            light.LightViewProjectionPositiveX = LightViewProjection;
                            break;
                        }
                        case CubeMapFace.PositiveY:
                        {
                            LightView = Matrix.CreateLookAt(light.Position, light.Position + Vector3.Up,
                                Vector3.Backward);

                            LightViewProjection = LightView*LightProjection;

                            light.LightViewProjectionPositiveY = LightViewProjection;
                            break;
                        }
                        case CubeMapFace.PositiveZ:
                        {
                            LightView = Matrix.CreateLookAt(light.Position, light.Position + Vector3.Forward, Vector3.Up);

                            LightViewProjection = LightView*LightProjection;

                            light.LightViewProjectionPositiveZ = LightViewProjection;

                            break;
                        }
                    }

                    if (_boundingFrustumShadow != null)
                        _boundingFrustumShadow.Matrix = LightViewProjection;
                    else
                        _boundingFrustumShadow = new BoundingFrustum(LightViewProjection);

                    meshMaterialLibrary.FrustumCulling(entities, _boundingFrustumShadow, true, light.Position);

                    // Rendering!

                    _graphicsDevice.SetRenderTarget(light.shadowMapCube, cubeMapFace);
                    _graphicsDevice.Clear(ClearOptions.DepthBuffer, Color.White, 1, 0);

                    meshMaterialLibrary.Draw(MeshMaterialLibrary.RenderType.shadowVSM, _graphicsDevice, LightViewProjection, true, light.HasChanged);
                }
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    // render the scene to all cubemap faces
                    cubeMapFace = (CubeMapFace)i;

                    switch (cubeMapFace)
                    {
                        case CubeMapFace.NegativeX:
                        {
                            LightViewProjection = light.LightViewProjectionNegativeX;
                            break;
                        }
                        case CubeMapFace.NegativeY:
                        {
                            LightViewProjection = light.LightViewProjectionNegativeY;
                            break;
                        }
                        case CubeMapFace.NegativeZ:
                        {
                            LightViewProjection = light.LightViewProjectionNegativeZ;
                            break;
                        }
                        case CubeMapFace.PositiveX:
                        {
                            LightViewProjection = light.LightViewProjectionPositiveX;
                            break;
                        }
                        case CubeMapFace.PositiveY:
                        {
                            LightViewProjection = light.LightViewProjectionPositiveY;
                            break;
                        }
                        case CubeMapFace.PositiveZ:
                        {
                            LightViewProjection = light.LightViewProjectionPositiveZ;

                            break;
                        }
                    }

                    if (_boundingFrustumShadow != null)
                        _boundingFrustumShadow.Matrix = LightViewProjection;
                    else
                        _boundingFrustumShadow = new BoundingFrustum(LightViewProjection);

                    bool hasAnyObjectMoved = meshMaterialLibrary.FrustumCulling(entities, _boundingFrustumShadow, false, light.Position);

                    // Rendering!

                    if (!hasAnyObjectMoved) continue;

                    _graphicsDevice.SetRenderTarget(light.shadowMapCube, cubeMapFace);

                    meshMaterialLibrary.Draw(renderType: MeshMaterialLibrary.RenderType.shadowVSM, 
                        graphicsDevice: _graphicsDevice,
                        viewProjection: LightViewProjection,
                        lightViewPointChanged: light.HasChanged,
                        hasAnyObjectMoved: true);
                }

            //Culling!


            }

        }

        #endregion

        #region draw

        private void DrawHolograms(MeshMaterialLibrary meshMat)
        {
            if (!GameSettings.g_HologramDraw) return;
            _graphicsDevice.SetRenderTarget(_renderTargetHologram);
            _graphicsDevice.Clear(Color.Black);
            meshMat.Draw(MeshMaterialLibrary.RenderType.hologram, _graphicsDevice, _viewProjection);
        }

        private void DrawEnvironmentMap(Vector3 cameraPosition)
        {
            if (!GameSettings.g_EnvironmentMapping) return;

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            Shaders.deferredEnvironmentParameterInverseViewProjection.SetValue(_inverseViewProjection);

           Shaders.deferredEnvironmentParameterCameraPosition.SetValue(cameraPosition);

            Shaders.deferredEnvironment.CurrentTechnique = GameSettings.g_SSR
                ? Shaders.deferredEnvironment.Techniques["g_SSR"]
                : Shaders.deferredEnvironment.Techniques["Classic"];
            Shaders.deferredEnvironment.CurrentTechnique.Passes[0].Apply();
            _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);
        }

        /// <summary>
        /// Draw all light sources in a deferred Renderer!
        /// </summary>
        /// <param name="pointLights"></param>
        /// <param name="dirLights"></param>
        /// <param name="cameraOrigin"></param>
        /// <param name="camera"></param>
        private void DrawLights(List<PointLight> pointLights, List<DirectionalLight> dirLights, Vector3 cameraOrigin)
        {
            _graphicsDevice.SetRenderTargets(_renderTargetLightBinding);
            _graphicsDevice.Clear(Color.TransparentBlack);
            DrawPointLights(pointLights, cameraOrigin);
            DrawDirectionalLights(dirLights, cameraOrigin);
        }

        private void DrawDirectionalLights(List<DirectionalLight> dirLights, Vector3 cameraOrigin)
        {
            if (dirLights.Count < 1) return;

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            //If nothing has changed we don't need to update
            if (viewProjectionHasChanged)
            {
                Shaders.deferredDirectionalLightParameterViewProjection.SetValue(_viewProjection);
                Shaders.deferredDirectionalLightParameterCameraPosition.SetValue(cameraOrigin);
                Shaders.deferredDirectionalLightParameterInverseViewProjection.SetValue(_inverseViewProjection);
            }

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;

            for (int index = 0; index < dirLights.Count; index++)
            {
                DirectionalLight light = dirLights[index];
                DrawDirectionalLight(light);
            }
        }

        private void DrawDirectionalLight(DirectionalLight light)
        {
            Shaders.deferredDirectionalLightParameter_LightColor.SetValue(light.Color.ToVector3());
            Shaders.deferredDirectionalLightParameter_LightDirection.SetValue(light.Direction);
            Shaders.deferredDirectionalLightParameter_LightIntensity.SetValue(light.Intensity);
            light.ApplyShader();
            _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);
        }

        //Draw the pointlights
        private void DrawPointLights( List<PointLight> pointLights,Vector3 cameraOrigin)
        {
            _graphicsDevice.BlendState = _lightBlendState;

            //If nothing has changed we don't need to update
            if (viewProjectionHasChanged)
            {
                Shaders.deferredPointLightParameterViewProjection.SetValue(_viewProjection);
                Shaders.deferredPointLightParameterCameraPosition.SetValue(cameraOrigin);
                Shaders.deferredPointLightParameterInverseViewProjection.SetValue(_inverseViewProjection);
            }

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;

            for (int index = 0; index < pointLights.Count; index++)
            {
                PointLight light = pointLights[index];
                DrawPointLight(light, cameraOrigin);
            }
        }

        //Draw each individual Point light
        private void DrawPointLight(PointLight light, Vector3 cameraOrigin)
        {
            //todo: Optimize the culling and do it with the HasChanged thing like the models!
            //first let's check if the light is even in bounds
            if (_boundingFrustum.Contains(light.BoundingSphere) == ContainmentType.Disjoint ||
                !_boundingFrustum.Intersects(light.BoundingSphere))
                return;

            GameStats.LightsDrawn ++;

            Matrix sphereWorldMatrix = Matrix.CreateScale(light.Radius * 1.2f) * Matrix.CreateTranslation(light.Position);
            Shaders.deferredPointLightParameter_World.SetValue(sphereWorldMatrix);

            //light position
            Shaders.deferredPointLightParameter_LightPosition.SetValue(light.Position);
            //set the color, radius and Intensity
            Shaders.deferredPointLightParameter_LightColor.SetValue(light.Color.ToVector3());
            Shaders.deferredPointLightParameter_LightRadius.SetValue(light.Radius);
            Shaders.deferredPointLightParameter_LightIntensity.SetValue(light.Intensity);
            //parameters for specular computations

            float cameraToCenter = Vector3.Distance(cameraOrigin, light.Position);

            bool inside = cameraToCenter < light.Radius*1.2f;
            Shaders.deferredPointLightParameter_Inside.SetValue(inside);

            _graphicsDevice.RasterizerState = inside ? RasterizerState.CullClockwise : RasterizerState.CullCounterClockwise;
            
            Shaders.deferredPointLight.CurrentTechnique.Passes[0].Apply();

            foreach (ModelMesh mesh in _assets.Sphere.Meshes)
            {
                foreach (ModelMeshPart meshpart in mesh.MeshParts)
                {
                    light.ApplyShader();
                    _graphicsDevice.SetVertexBuffer(meshpart.VertexBuffer);
                    _graphicsDevice.Indices = (meshpart.IndexBuffer);
                    int primitiveCount = meshpart.PrimitiveCount;
                    int vertexOffset = meshpart.VertexOffset;
                    int startIndex = meshpart.StartIndex;

                    _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, vertexOffset, startIndex, primitiveCount);
                }
            }
        }

        private void DrawMapToScreenToCube(RenderTarget2D map, RenderTargetCube target, CubeMapFace? face)
        {

            if (face != null) _graphicsDevice.SetRenderTarget(target, (CubeMapFace)face);
           // _graphicsDevice.Clear(Color.CornflowerBlue);
            _spriteBatch.Begin(0, BlendState.Opaque, SamplerState.PointClamp);
            _spriteBatch.Draw(map, new Rectangle(0, 0, map.Width, map.Height), Color.White);
            _spriteBatch.End();
        }

        private void DrawMapToScreenToFullScreen(Texture2D map, BlendState blendState = null)
        {
            if(blendState == null) blendState = BlendState.Opaque;

            int height;
            int width;
            if (Math.Abs(map.Width / (float)map.Height - GameSettings.g_ScreenWidth / (float)GameSettings.g_ScreenHeight) < 0.001)
            //If same aspectratio
            {
                height = GameSettings.g_ScreenHeight;
                width = GameSettings.g_ScreenWidth;
            }
            else
            {
                if (GameSettings.g_ScreenHeight < GameSettings.g_ScreenWidth)
                {
                    height = GameSettings.g_ScreenHeight;
                    width = GameSettings.g_ScreenHeight;
                }
                else
                {
                    height = GameSettings.g_ScreenWidth;
                    width = GameSettings.g_ScreenWidth;
                }
            }
            _graphicsDevice.SetRenderTarget(null);
            _spriteBatch.Begin(0, blendState, SamplerState.PointClamp, null, null);
            _spriteBatch.Draw(map, new Rectangle(0, 0, width, height), Color.White);
            _spriteBatch.End();
        }

        private void Compose()
        {
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            if (GameSettings.g_TemporalAntiAliasing)
            {
                _graphicsDevice.SetRenderTargets(_temporalAAOffFrame ? _renderTargetFinal2Binding : _renderTargetFinalBinding);
            }
            else
            {
                _graphicsDevice.SetRenderTargets(_renderTargetFinalBinding);
            }

            //Skull depth
            //_deferredCompose.Parameters["average_skull_depth"].SetValue(Vector3.Distance(camera.Position , new Vector3(29, 0, -6.5f)));


            //combine!
            Shaders.DeferredCompose.CurrentTechnique.Passes[0].Apply();
            _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);
        }

        #endregion

        #region draw GBuffer
        ///////////////////////////////////////////// Draw to GBuffer /////////////////////////////////////////////////////////////////

        /// <summary>
        /// Draw models and everything to the GBuffer
        /// </summary>
        /// <param name="meshMaterialLibrary"></param>
        /// <param name="entities"></param>
        public void DrawGBuffer(MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities)
        {
            //current state should be
            //blendstate.opaque
            //defaultDepthStencilState
            //renderTarget = albedo/normal/depth_v

            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            meshMaterialLibrary.Draw(renderType: MeshMaterialLibrary.RenderType.opaque, graphicsDevice: _graphicsDevice, viewProjection: _viewProjection, lightViewPointChanged: true);
        }

        #endregion

        #region setup draw
        ///////////////////////////////////////////// Set up /////////////////////////////////////////////////////////////////


        private void ResetStats()
        {
            GameStats.MaterialDraws = 0;
            GameStats.MeshDraws = 0;
            GameStats.LightsDrawn = 0;
            GameStats.shadowMaps = 0;
            GameStats.activeShadowMaps = 0;

            GameStats.EmissiveMeshDraws = 0;
        }


        private void CheckRenderChanges(List<DirectionalLight> dirLights)
        {
            //Check if supersampling has changed
            if (_supersampling != GameSettings.g_supersampling)
            {
                _supersampling = GameSettings.g_supersampling;
                SetUpRenderTargets(GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight, false);
            }

            if (_hologramDraw != GameSettings.g_HologramDraw)
            {
                _hologramDraw = GameSettings.g_HologramDraw;

                if (!_hologramDraw)
                {
                    _graphicsDevice.SetRenderTarget(_renderTargetHologram);
                    _graphicsDevice.Clear(Color.Black);
                }
            }

            if (_forceShadowFiltering != GameSettings.g_ShadowForceFiltering)
            {
                _forceShadowFiltering = GameSettings.g_ShadowForceFiltering;

                foreach (DirectionalLight light in dirLights)
                {
                    if(light.ShadowMap!=null) light.ShadowMap.Dispose();
                    light.ShadowMap = null;

                    light.ShadowFiltering = (DirectionalLight.ShadowFilteringTypes) (_forceShadowFiltering - 1);

                    light.HasChanged = true;
                }
            }

            if (_forceShadowSS != GameSettings.g_ShadowForceScreenSpace)
            {
                _forceShadowSS = GameSettings.g_ShadowForceScreenSpace;

                foreach (DirectionalLight light in dirLights)
                {

                    light.ScreenSpaceShadowBlur = _forceShadowSS;

                    light.HasChanged = true;
                }
            }


        }


        /// <summary>
        /// Set up the view projection matrices
        /// </summary>
        /// <param name="camera"></param>
        private void UpdateViewProjection(Camera camera, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities)
        {
            viewProjectionHasChanged = camera.HasChanged;

            if (GameSettings.g_TemporalAntiAliasing)
            {
                viewProjectionHasChanged = true;
                _temporalAAOffFrame = !_temporalAAOffFrame;
                _temporalAAFrame++;

                //if (_temporalAAFrame >= 4) _temporalAAFrame = 0;
            }

            //If the camera didn't do anything we don't need to update this stuff
            if (viewProjectionHasChanged)
            {
                camera.HasChanged = false;

                _view = Matrix.CreateLookAt(camera.Position, camera.Lookat, camera.Up);

                _projection = Matrix.CreatePerspectiveFieldOfView(camera.FieldOfView,
                    GameSettings.g_ScreenWidth / (float)GameSettings.g_ScreenHeight, 1, GameSettings.g_FarPlane);

                Shaders.GBufferEffectParameter_View.SetValue(_view);

                
                _viewProjection = _view*_projection;
                _staticViewProjection = _viewProjection;

                _currentToPrevious = Matrix.Invert(_viewProjection)*_previousViewProjection;

                _previousViewProjection = _viewProjection;

                if (GameSettings.g_TemporalAntiAliasing)
                {
                    if (GameSettings.g_TemporalAntiAliasingJitterMode == 0)
                    {
                        //float x = 0.5f/GameSettings.g_ScreenWidth;
                        //float y = 0.5f/GameSettings.g_ScreenHeight;

                        //Vector3 translation = new Vector3(
                        //    x: _temporalAAFrame > 1 ? x : -x,
                        //    y: _temporalAAFrame %2 ==0 ? y : -y, z: 0);

                        //_viewProjection = _viewProjection*
                        //                  Matrix.CreateTranslation(translation);

                        float translation = _temporalAAOffFrame ? 0.5f : -0.5f;

                        _viewProjection = _viewProjection *
                                          Matrix.CreateTranslation(new Vector3(translation / GameSettings.g_ScreenWidth,
                                              translation / GameSettings.g_ScreenHeight, 0));
                    }
                    else
                    {
                        //Create a random direction!
                        float randomAngle = FastRand.NextAngle();

                        Vector3 translation = new Vector3((float)Math.Sin(randomAngle) / GameSettings.g_ScreenWidth, (float)Math.Cos(randomAngle) / GameSettings.g_ScreenHeight, 0) * 0.5f;

                        _viewProjection = _viewProjection *
                                          Matrix.CreateTranslation(translation);

                    }
                }

                _inverseViewProjection = Matrix.Invert(_viewProjection);


                if (_boundingFrustum == null)
                {
                    _boundingFrustum = new BoundingFrustum(_viewProjection);
                }
                else
                {
                    _boundingFrustum.Matrix = _viewProjection;
                }


            }

            //We need to update whether or not entities are in our boundingFrustum and then cull them or not!
            meshMaterialLibrary.FrustumCulling(entities, _boundingFrustum, viewProjectionHasChanged, camera.Position);

        }

        /// <summary>
        /// Initialize our GBuffer
        /// </summary>
        private void SetUpGBuffer()
        {
            _graphicsDevice.SetRenderTargets(_renderTargetBinding);

            _graphicsDevice.RasterizerState = RasterizerState.CullNone;
            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            //_graphicsDevice.Clear(ClearOptions.Stencil | ClearOptions.Target | ClearOptions.DepthBuffer, Color.DarkOrange, 1.0f, 0);

            //Clear the GBuffer
            Shaders.ClearGBufferEffect.CurrentTechnique.Passes[0].Apply();
            _quadRenderer.RenderQuad(_graphicsDevice, Vector2.One * -1, Vector2.One);

            //todo: check if the above is already ccw
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        }

        #endregion

        #region RenderTargetControl


        public void UpdateResolution()
        {
            SetUpRenderTargets(GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight, false);
        }

        private void SetUpRenderTargets(int width, int height, bool onlyEssentials)
        {
            //Discard first

            if (_renderTargetAlbedo != null)
            {
                _renderTargetAlbedo.Dispose();
                _renderTargetDepth.Dispose();
                _renderTargetNormal.Dispose();
                _renderTargetFinal.Dispose();
                _renderTargetDiffuse.Dispose();
                _renderTargetSpecular.Dispose();

                _renderTargetScreenSpaceEffectPrepareBlur.Dispose();

                if (!onlyEssentials)
                {
                    _renderTargetHologram.Dispose();
                    _renderTargetFinal2.Dispose();
                    _renderTargetScreenSpaceEffect.Dispose();
                    _renderTargetScreenSpaceEffect2.Dispose();

                    _renderTargetScreenSpaceEffectBlurX.Dispose();
                    _renderTargetScreenSpaceEffectBlurYFinal.Dispose();

                    _renderTargetEmissive.Dispose();
                }
            }


            //DEFAULT

            float ssmultiplier = _supersampling;

            int target_width = (int) (width * ssmultiplier);
            int target_height = (int) (height * ssmultiplier);

            Shaders.BillboardEffectParameter_AspectRatio.SetValue((float)target_width / target_height);

            _renderTargetAlbedo = new RenderTarget2D(_graphicsDevice, target_width,
                target_height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

            _renderTargetNormal = new RenderTarget2D(_graphicsDevice, target_width,
                target_height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _renderTargetDepth = new RenderTarget2D(_graphicsDevice, target_width,
                target_height, false, SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            //Half res!

            _renderTargetBinding[0] = new RenderTargetBinding(_renderTargetAlbedo);
            _renderTargetBinding[1] = new RenderTargetBinding(_renderTargetNormal);
            _renderTargetBinding[2] = new RenderTargetBinding(_renderTargetDepth);

            _renderTargetDiffuse = new RenderTarget2D(_graphicsDevice, target_width,
               target_height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

            _renderTargetSpecular = new RenderTarget2D(_graphicsDevice, target_width,
               target_height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

            _renderTargetLightBinding[0] = new RenderTargetBinding(_renderTargetDiffuse);
            _renderTargetLightBinding[1] = new RenderTargetBinding(_renderTargetSpecular);

            _renderTargetFinal = new RenderTarget2D(_graphicsDevice, target_width,
               target_height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _renderTargetFinalBinding[0] = new RenderTargetBinding(_renderTargetFinal);

            _renderTargetScreenSpaceEffectPrepareBlur = new RenderTarget2D(_graphicsDevice, target_width,
                target_height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

            if (!onlyEssentials)
            {
                _editorRender.SetUpRenderTarget(width, height);

                _renderTargetFinal2 = new RenderTarget2D(_graphicsDevice, target_width,
                    target_height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

                _renderTargetFinal2Binding[0] = new RenderTargetBinding(_renderTargetFinal2);

                // Shaders.SSReflectionEffectParameter_Resolution.SetValue(new Vector2(target_width, target_height));
                Shaders.deferredPointLightParameterResolution.SetValue(new Vector2(target_width, target_height));
                Shaders.EmissiveEffectParameter_Resolution.SetValue(new Vector2(target_width, target_height));

                _renderTargetEmissive = new RenderTarget2D(_graphicsDevice, target_width,
                    target_height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

                _renderTargetScreenSpaceEffect2 = new RenderTarget2D(_graphicsDevice, target_width,
                    target_height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

                _renderTargetScreenSpaceEffectBlurX = new RenderTarget2D(_graphicsDevice, target_width,
                    target_height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

                _renderTargetScreenSpaceEffectBlurYFinal = new RenderTarget2D(_graphicsDevice, target_width,
                    target_height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

                Shaders.ScreenSpaceEffectParameter_InverseResolution.SetValue(new Vector2(1.0f/target_width,
                    1.0f/target_height));

                ///////////////////
                /// HALF RESOLUTION


                target_width /= 2;
                target_height /= 2;

                _renderTargetScreenSpaceEffect = new RenderTarget2D(_graphicsDevice, target_width,
                    target_height, false, SurfaceFormat.HalfSingle, DepthFormat.None, 0,
                    RenderTargetUsage.DiscardContents);


                _renderTargetHologram = new RenderTarget2D(_graphicsDevice, target_width,
                    target_height, false, SurfaceFormat.Single, DepthFormat.Depth24, 0,
                    RenderTargetUsage.PreserveContents);
            }

            UpdateRenderMapBindings(onlyEssentials);
        }

        private void UpdateRenderMapBindings(bool onlyEssentials)
        {
            Shaders.BillboardEffectParameter_DepthMap.SetValue(_renderTargetDepth);

            Shaders.deferredPointLightParameter_AlbedoMap.SetValue(_renderTargetAlbedo);
            Shaders.deferredPointLightParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.deferredPointLightParameter_NormalMap.SetValue(_renderTargetNormal);

            Shaders.deferredDirectionalLightParameter_AlbedoMap.SetValue(_renderTargetAlbedo);
            Shaders.deferredDirectionalLightParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.deferredDirectionalLightParameter_NormalMap.SetValue(_renderTargetNormal);

            Shaders.deferredDirectionalLightParameter_SSShadowMap.SetValue(onlyEssentials ? _renderTargetScreenSpaceEffectPrepareBlur : _renderTargetScreenSpaceEffectBlurYFinal);

            Shaders.deferredEnvironmentParameter_AlbedoMap.SetValue(_renderTargetAlbedo);
            //Shaders.deferredEnvironmentParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.deferredEnvironmentParameter_NormalMap.SetValue(_renderTargetNormal);

            Shaders.DeferredComposeEffectParameter_ColorMap.SetValue(_renderTargetAlbedo);
            Shaders.DeferredComposeEffectParameter_diffuseLightMap.SetValue(_renderTargetDiffuse);
            Shaders.DeferredComposeEffectParameter_specularLightMap.SetValue(_renderTargetSpecular);
            Shaders.DeferredComposeEffectParameter_SSAOMap.SetValue(_renderTargetScreenSpaceEffectBlurYFinal);
            Shaders.DeferredComposeEffectParameter_HologramMap.SetValue(_renderTargetHologram);

            Shaders.ScreenSpaceEffectParameter_NormalMap.SetValue(_renderTargetNormal);
            Shaders.ScreenSpaceEffectParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetScreenSpaceEffect);

            Shaders.ScreenSpaceEffect2Parameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.ScreenSpaceEffect2Parameter_NormalMap.SetValue(_renderTargetNormal);
            Shaders.ScreenSpaceEffect2Parameter_TargetMap.SetValue(_renderTargetFinal);

            Shaders.EmissiveEffectParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.EmissiveEffectParameter_EmissiveMap.SetValue(_renderTargetEmissive);
            Shaders.EmissiveEffectParameter_NormalMap.SetValue(_renderTargetNormal);

            Shaders.TemporalAntiAliasingEffect_DepthMap.SetValue(_renderTargetDepth);
        }

        #endregion

    }
}

