using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace RE2Extended.Runtime.ConstructionManager
{
    // Optional reliable opener: a small owned UGUI button shown beside the
    // native Escape menu. It borrows only the native menu's font metadata.
    internal sealed class ConstructionManagerMenuEntry : IDisposable
    {
        private GameObject surface;
        private RectTransform rect;
        private TextMeshProUGUI label;

        internal bool Clicked;

        internal void Sync(GameObject nativeMenu, bool eligible)
        {
            if (!eligible || nativeMenu == null)
            {
                Hide();
                return;
            }

            try
            {
                if (surface == null) Build(nativeMenu);

                float scale = Math.Max(0.85f, Math.Min(1.6f,
                    Math.Min(Screen.width / 1920f, Screen.height / 1080f)));
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.anchoredPosition = new Vector2(24f * scale, 24f * scale);
                rect.sizeDelta = new Vector2(230f * scale, 36f * scale);
                if (label != null) label.fontSize = 16f * scale;

                surface.SetActive(true);
            }
            catch
            {
                Hide();
            }
        }

        private void Build(GameObject nativeMenu)
        {
            surface = new GameObject("RE2Extended.ConstructionManagerEntry",
                typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(surface);

            Canvas canvas = surface.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            GameObject buttonObject = new GameObject("Construction Manager",
                typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(surface.transform, false);

            rect = buttonObject.GetComponent<RectTransform>();
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.045f, 0.16f, 0.25f, 0.96f);
            buttonObject.GetComponent<Button>().onClick.AddListener(() => Clicked = true);

            GameObject labelObject = new GameObject("Label",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 2f);
            labelRect.offsetMax = new Vector2(-8f, -2f);

            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "Construction Manager";
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            TMP_Text nativeText = nativeMenu.GetComponentInChildren<TMP_Text>();
            if (nativeText != null && nativeText.font != null)
                label.font = nativeText.font;

            surface.SetActive(false);
        }

        internal void Hide()
        {
            if (surface == null) return;
            try
            {
                EventSystem events = EventSystem.current;
                GameObject selected = events == null ? null : events.currentSelectedGameObject;
                if (selected != null && selected.transform.IsChildOf(surface.transform))
                    events.SetSelectedGameObject(null);
                surface.SetActive(false);
            }
            catch
            {
                // Hiding an optional opener must not affect the manager.
            }
        }

        public void Dispose()
        {
            Hide();
            try
            {
                if (surface != null) UnityEngine.Object.Destroy(surface);
            }
            catch
            {
                // Shutdown must not throw into the game.
            }
            finally
            {
                surface = null;
                rect = null;
                label = null;
            }
        }
    }
}
