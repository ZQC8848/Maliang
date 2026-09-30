
using UnityEngine;

public class PenStyles : MonoBehaviour
{
    public struct PenStruct
    {
        public string Name;
        public Texture img;
        public int Index;
    }

    public static PenStyles instance;

    public Texture[] Styles;

    private Texture _currentStyle;

    public Texture CurrentStyle
    {
        get
        {
            if (_currentStyle == null)
                _currentStyle = Styles[0];
            return _currentStyle;
        }

        set { _currentStyle = value; }
    }

    private void Awake()
    {
        instance = this;
    }

    public Texture ResetStyle()
    {
        CurrentStyle = Styles[0];
        return CurrentStyle;
    }

    public void RandomStyle()
    {
        int index = Random.Range(0, Styles.Length);
        CurrentStyle = Styles[index];
        PaintingHandler.instance.BrushStyle(CurrentStyle);
    }
}


