using System;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    public partial class LSRawImage : RawImage
    {
        private static readonly LSVertexHelper vertexHelper = new();

        static LSRawImage()
        {
            vertexHelper.Init();
        }

        /// <summary>Whether the texture keeps its aspect ratio inside the control rect.</summary>
        [SerializeField, StateProperty(nameof(SetVerticesDirty))] private bool preserveAspectRatio;
        [SerializeField, StateField(nameof(SetVerticesDirty))] private int rotateId = 0;
        [SerializeField, StateField(nameof(SetVerticesDirty))] private Vector2Int flip;

        public (bool x, bool y) Flip
        {
            get => (flip.x.ToBool(),  flip.y.ToBool());
            set => SetFlipState(new Vector2Int(value.x.ToInt(), value.y.ToInt()));
        }

        public RotationMode Rotation
        {
            get => (RotationMode)rotateId;
            set => SetRotateIdState((int)value);
        }

        /// <inheritdoc/>
        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();
            SetVerticesDirty();
        }

        protected override void UpdateGeometry()
        {
            DoMeshGeneration();
        }

        private void DoMeshGeneration()
        {
            Action<Mesh> fillMesh = vertexHelper.FillMeshUI;
            vertexHelper.Clear();

            if (rectTransform != null && rectTransform.rect is { width: > 0, height: > 0 })
            {
                OnPopulateMesh(vertexHelper);
            }

            var mesh = workerMesh;
            fillMesh(mesh);
            OnMeshFilled(mesh);
            canvasRenderer.SetMesh(mesh);
        }

        public Rect MeshRect
        {
            get
            {
                float texAspect = Aspect;
                Rect r = GetPixelAdjustedRect();

                Vector2 pivot = rectTransform.pivot;

                float newWidth, newHeight;
                float rAspect = r.width / r.height;
                if (rAspect > texAspect)
                {
                    newHeight = r.height;
                    newWidth = newHeight * texAspect;
                }
                else
                {
                    newWidth = r.width;
                    newHeight = newWidth / texAspect;
                }

                float offsetX = r.x + r.width * pivot.x - newWidth * pivot.x;
                float offsetY = r.y + r.height * pivot.y - newHeight * pivot.y;

                float xMin = offsetX;
                float yMin = offsetY;
                float xMax = offsetX + newWidth;
                float yMax = offsetY + newHeight;
                return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            }
        }

        /// <summary>
        /// The assigned <see cref="RawImage.texture"/>, or <see langword="null"/> when none is set.
        /// </summary>
        /// <remarks>
        /// Neither falls back to the material's <c>_MainTex</c> the way <see cref="RawImage"/> does, nor
        /// substitutes a white stand-in the way <see cref="Graphic"/> does. LightSide surface shaders
        /// publish their textures as global bindings and declare no <c>_MainTex</c>; the Canvas batch key
        /// pairs material with this texture, so every LightSide graphic reporting nothing is what lets
        /// text, shapes and vector animation share one batch. Reading the material property it does not
        /// declare would also log an error on every geometry update.
        /// </remarks>
        public override Texture mainTexture => texture;

        /// <summary>Aspect of the assigned texture, or 1 when the surface draws without one.</summary>
        protected virtual float Aspect => texture != null ? texture.AspectRatio() : 1f;

        protected virtual void OnMeshFilled(Mesh mesh){}

        protected void OnPopulateMesh(LSVertexHelper vh)
        {
            Texture tex = texture;
            vh.Clear();

            if (preserveAspectRatio)
            {
                var meshRect = MeshRect;
                Color32 color32 = color;
                vh.AddVert(new Vector3(meshRect.xMin, meshRect.yMin), color32, new Vector2(0, 0));
                vh.AddVert(new Vector3(meshRect.xMin, meshRect.yMax), color32, new Vector2(0, 1));
                vh.AddVert(new Vector3(meshRect.xMax, meshRect.yMax), color32, new Vector2(1, 1));
                vh.AddVert(new Vector3(meshRect.xMax, meshRect.yMin), color32, new Vector2(1, 0));

                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(2, 3, 0);
            }
            else
            {
                var r = GetPixelAdjustedRect();
                var v = new Vector4(r.x, r.y, r.x + r.width, r.y + r.height);
                var scaleX = tex != null ? tex.width * tex.texelSize.x : 1f;
                var scaleY = tex != null ? tex.height * tex.texelSize.y : 1f;
                {
                    var color32 = color;
                    vh.AddVert(new Vector3(v.x, v.y), color32, new Vector2(0, 0));
                    vh.AddVert(new Vector3(v.x, v.w), color32, new Vector2(0, scaleY));
                    vh.AddVert(new Vector3(v.z, v.w), color32, new Vector2(scaleX, scaleY));
                    vh.AddVert(new Vector3(v.z, v.y), color32, new Vector2(scaleX, 0));

                    vh.AddTriangle(0, 1, 2);
                    vh.AddTriangle(2, 3, 0);
                }
            }

            PostProcessMesh(vh);
        }

        private void PostProcessMesh(LSVertexHelper vh)
        {
            RotateMesh(vh);
        }

        private void RotateMesh(LSVertexHelper vh)
        {
            vh.ApplyRotateFlip(rotateId, flip, rectTransform.rect.center * 2);
        }
    }
}
