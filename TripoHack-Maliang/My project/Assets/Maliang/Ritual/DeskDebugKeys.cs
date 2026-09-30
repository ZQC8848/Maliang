using System.IO;
using Maliang.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maliang.Ritual
{
    /// <summary>
    /// Development shortcuts for the desk: keyboard (editor / desktop) and the left controller's menu button.
    /// R or left menu: reset the scroll.   E: export the ink to TestData/Exports/ as PNG.
    /// </summary>
    public class DeskDebugKeys : MonoBehaviour
    {
        [Tooltip("Resets / exports the station's active scroll (and clears away earlier ones). Without it, the ritual below.")]
        public ScrollStation station;
        public ScrollRitual ritual;

        ScrollRitual Active => station != null && station.Active != null ? station.Active : ritual;

        InputAction _reset;

        void OnEnable()
        {
            _reset = new InputAction(type: InputActionType.Button);
            _reset.AddBinding("<Keyboard>/r");
            _reset.AddBinding("<XRController>{LeftHand}/menuButton");
            _reset.Enable();
        }

        void OnDisable() => _reset?.Dispose();

        void Update()
        {
            if (Active == null) return;
            if (_reset.WasPressedThisFrame())
            {
                if (station != null) station.ResetAll();
                else ritual.ResetScroll();
            }
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ExportToTestData();
        }

        [ContextMenu("Export Ink To TestData")]
        public void ExportToTestData()
        {
            Active.canvas.Export(result =>
            {
                if (result.Empty) return;
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestData", "Exports"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"ink_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
                File.WriteAllBytes(path, result.Png);
                MaliangLog.Info("Debug", "Wrote " + path);
            });
        }
    }
}
