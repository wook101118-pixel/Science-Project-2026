using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace sung_wook
{
    /// <summary>
    /// Game View 캡처가 불가능할 때 월드 스프라이트만 렌더링하는 검증 보조 도구.
    /// 이 대체 캡처에는 IMGUI 패널/화살표 글자가 포함되지 않으므로 전체 화면 검사와 구분한다.
    /// </summary>
    public static class ElectricLearningVisualCheck
    {
        public static bool CaptureWorld(ElectricStageSelector selector, string path,
            int width = 1280, int height = 720)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("World capture requires the configured scene in Play Mode.");
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("World capture requires a graphics device; omit -nographics.");
            Camera camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("The active scene has no enabled MainCamera.");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A PNG output path is required.", nameof(path));

            selector.GetComponent<ElectricVectorDisplay2D>()?.RefreshDisplay();
            Physics2D.SyncTransforms();
            width = Mathf.Clamp(width, 64, 4096);
            height = Mathf.Clamp(height, 64, 4096);
            string absolutePath = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = null;
            Texture2D pixels = null;
            try
            {
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Electric Learning Verification Capture",
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                if (GraphicsSettings.currentRenderPipeline == null)
                {
                    camera.targetTexture = target;
                    camera.Render();
                }
                else if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
                {
                    // 프로젝트의 URP가 지원하는 단일 카메라 요청으로 화면 창 없이 렌더링한다.
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    throw new InvalidOperationException("The active pipeline is not the project's URP or built-in pipeline.");
                }

                RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                pixels.Apply(false, false);
                byte[] png = pixels.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("The world render did not produce PNG data.");
                File.WriteAllBytes(absolutePath, png);
                Debug.Log("Electric learning world capture (excludes OnGUI): " + absolutePath);
                return File.Exists(absolutePath) && new FileInfo(absolutePath).Length > 0;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }
    }
}
