using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
//1.激活画笔

//2.设置画笔颜色
//3.设置画笔大小
//4.设置画笔的样式 
public class PenBase : MonoBehaviour
{
    [Header("当前是否激活")]
    public bool isCurrentActive = false;
    public Color burshColor = Color.black;
    [Header("笔尖距离画布有效范围")]
    public Vector2 DicMinMax = new Vector2(0.98f, 1);
    [Header("画笔粗细最小--最大范围")]
    public Vector2 BrushMinMax =new Vector2(0.03f,1);
    [Header("射线检测长度")]
    public float rayLenght = 0.05f;//射线长度
    [Header("笔尖对象")]
    public Transform nib;//笔尖对象，射线是从笔尖对象的位置发出的，也是用他的位置来计算与画布的距离
    [Header("画布的layer")]
    public LayerMask layer;//画布的layer
    [HideInInspector]
    public bool isDraw=false;//是否正在写字
    [Header("笔头材质如果存在就随墨水的颜色改变")]
    public Material nibMat;//笔头材质，随着沾墨水的颜色改变

    //测试用
    bool isTestBool = false;

    private void Start()
    {
        //测试用
        isTestBool = isCurrentActive;
    }

    public virtual void Init()
    {
        isCurrentActive = true;

        SetBurshPen();

        SetBurshStyle( PenStyles.instance.CurrentStyle);

        SetBurshColor(burshColor);
    }

    /// <summary>
    /// 设置自身为当前使用的pen
    /// </summary>
    protected virtual void SetBurshPen()
    {
        PaintingHandler.instance.BrushSetPen(this);
    }

    /// <summary>
    /// 取消自身的激活属性
    /// </summary>
    public virtual void DisablePen()
    {
        isCurrentActive = false;
    }

    /// <summary>
    /// 设置笔触样式
    /// </summary>
    /// <param name="Img"></param>
    public virtual void SetBurshStyle(Texture Img)
    {
        if (!isCurrentActive) return;
        PaintingHandler.instance.BrushStyle(Img);
    }
    /// <summary>
    /// 设置笔触颜色
    /// </summary>
    /// <param name="Img"></param>
    public virtual void SetBurshColor(Color color)
    {
        if (!isCurrentActive) return;
        PaintingHandler.instance.BrushColor(color);
        burshColor = color;

        if (nibMat != null)
            nibMat.color = color;
    }
    /// <summary>
    /// 设置笔触大小
    /// </summary>
    /// <param name="BurshSize"></param>
    public virtual void SetBurshSize(float BurshSize)
    {
        if (!isCurrentActive) return;
        PaintingHandler.instance.BrushScale(BurshSize);
    }

    //切换到当前选择的笔触样式
    public virtual void GetBrushCurrentStyle()
    {
        PaintingHandler.instance.BrushStyle(PenStyles.instance.CurrentStyle);
    }

    /// <summary>
    /// 根据距离改变画笔大小
    /// </summary>
    protected virtual void ChangeBurshSize()
    {
        if (!isCurrentActive) return;

        Ray ray = new Ray(nib.transform.position, nib.forward);

        Debug.DrawRay(nib.position, nib.transform.forward * rayLenght, Color.red);

        if (Physics.Raycast(ray, rayLenght, layer))
        {
            float v = Vector3.Distance(nib.position, PaintingHandler.instance.minImg.position);
          //  print("转换前的距离：" + v);
            float v1 = GetDic(GetDic2(v));
          //  print("转换后的距离：" + v1);
            if (v1 >= 0)
            {
                SetBurshSize(v1);
                PaintingHandler.instance.BrushBeginDraw();
                isDraw = true;
            }
            else
            {
                //结束当前画画
                PaintingHandler.instance.BrushEndDraw();
                isDraw = false;
            }

        }
        else
        {
            PaintingHandler.instance.BrushEndDraw();
            isDraw = false;
        }

    }


    //线性方程
    protected float GetDic(float x)
    {
        return (BrushMinMax.y - BrushMinMax.x) / (DicMinMax.y - DicMinMax.x) * x + (BrushMinMax.y - (BrushMinMax.y - BrushMinMax.x) / (DicMinMax.y - DicMinMax.x) * DicMinMax.y);
    }

    protected float GetDic1(float x)
    {
        return (BrushMinMax.x - BrushMinMax.y) / (DicMinMax.x - DicMinMax.y) * x + (BrushMinMax.x - (BrushMinMax.x - BrushMinMax.y) / (DicMinMax.x - DicMinMax.y) * DicMinMax.x);
    }



    //将指数函数带入到线性方程
    protected float GetDic2(float x)
    {
        return Mathf.Exp(-x);
    }

    /// <summary>
    /// 测试用，isCurrentActive为True自动调用Init。后续使用中，当抓取pen的时候调用pen的Init函数即可，松开时不需要任何调用
    /// </summary>
    public  void ListenActiveSelf()
    {
        if (isTestBool != isCurrentActive)
        {
            isTestBool = isCurrentActive;

            if (isCurrentActive)
            {
                Init();
            }
        }
    }

    [ContextMenu("测试Color")]
    public void Test_ChangeColor()
    {
        SetBurshColor(burshColor);
    }

}
