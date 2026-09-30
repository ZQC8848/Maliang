using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pen3dUtility : MonoBehaviour
{
     

    /// <summary>
    /// 激活一个笔
    /// </summary>
    /// <param name="pen"></param>
    public void SetPen(PenBase pen)
    {
        PaintingHandler.instance.BrushSetPen(pen);
    }

    /// <summary>
    /// 设置毛笔的颜色
    /// </summary>
    /// <param name="color"></param>
    public void SetPenColor(Color color)
    {
        PaintingHandler.instance.BrushColor(color);
    }
    /// <summary>
    /// 设置毛笔的笔触样式
    /// </summary>
    /// <param name="texture"></param>
    public void SetPenStyle(Texture texture)
    {
        PaintingHandler.instance.BrushStyle(texture);
    }

    /// <summary>
    /// 开始写字
    /// </summary>
    public void BeginDraw()
    {
        PaintingHandler.instance.BrushBeginDraw();
    }

    /// <summary>
    /// 结束写字
    /// </summary>
    public void EndDraw()
    {
        PaintingHandler.instance.BrushEndDraw();
    }
    
   
}
