
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class PaintingHandler : MonoBehaviour
{
    public static PaintingHandler instance;

    private void Awake()
    {
        instance = this;
    }

    #region parameters

    private RenderTexture mainRender;
    [Header("画布对象[RawImage]")]
    public RawImage raw;//锚点为自身
    [Header("存在画布子集用于获取位置信息")]
    public RectTransform minImg;//放置在raw子集下用于换算坐标
    public Material mat;
    public Texture brushPNGTexture;   //画笔笔触类型，需要png
    private float brushScale = 0.5f;
    Color brushCurrentColor = Color.black;
    private Vector3[] PositionArray = new Vector3[4];
    private int pointCout = 0;//贝塞尔中计算点的数量
    private float[] speedArray = new float[4];
    private int speedCout = 0;//根据点之间的差异改变画笔的内部大小

    float brushOutSize = 0.5f; //外部画笔大小

    float rawWidth;
    float rawHeight;
    bool isTarget = false;
    PenBase currentPen;//毛笔对象
    private Stack<RenderTexture> cacheList = new Stack<RenderTexture>(50);//堆栈缓存每一步的操作，如果要实现往回撤改用链表
    [HideInInspector]
    public bool isBurrs = true;//毛边
    #endregion


    void Start()
    {

        rawWidth = raw.rectTransform.sizeDelta.x;
        rawHeight = raw.rectTransform.sizeDelta.y;
        InitPositionInfo();
        mainRender = new RenderTexture((int)rawWidth, (int)rawHeight, 24, RenderTextureFormat.ARGB32);
        Clear();
    }

    #region ===============================Public Function================================

    /// <summary>
    /// 设置写字的对象(pen的笔尖对象不能为空,用于计算射线的开始位置)
    /// </summary>
    /// <param name="target"></param>
    public void BrushSetPen(PenBase pen)
    {
        if (currentPen != null)
        {
            currentPen.DisablePen();
            BrushEndDraw();
            OnReset();
            BrushStyle(PenStyles.instance.ResetStyle());
            isBurrs = true;
        }
        currentPen = pen;
        isTarget = true;
    }

    /// <summary>
    /// 开始写字
    /// </summary>
    public void BrushBeginDraw()
    {
        if (!isDraw)
            isDraw = true;
    }
    /// <summary>
    /// 结束写字
    /// </summary>
    public void BrushEndDraw()
    {
        if (isDraw)
            isDraw = false;
    }

    /// <summary>
    /// 设置画笔颜色
    /// </summary>
    /// <param name="color"></param>
    public void BrushColor(Color color)
    {
        brushCurrentColor = color;
    }

    /// <summary>
    /// 设置外部画笔大小
    /// </summary>
    /// <param name="v"></param>
    public void BrushScale(float v)
    {
        brushOutSize = v;
    }

    /// <summary>
    /// 设置笔触的风格
    /// </summary>
    /// <param name="texture"></param>
    public void BrushStyle(Texture texture)
    {
        brushPNGTexture = texture;
    }

    /// <summary>
    /// 撤销
    /// </summary>
    public void BrushBackUp()
    {
        BackDraw();
    }
    /// <summary>
    /// 清空
    /// </summary>
    public void BrushClear()
    {
        ClearDraw();
    }

    #endregion

    #region Private Function==============================
    private void InitPositionInfo()
    {
        opx = 0 - (-rawWidth / 2);
        opy = rawHeight - rawHeight / 2;
    }

    float opx, opy;

    private Vector2 GetPositionInfo()
    {
        float x = minImg.anchoredPosition.x;
        float y = minImg.anchoredPosition.y;

        float x1 = x + opx;
        float y1 = y + opy;

        return new Vector2(x1, y1);
    }

    Vector3 startPosition = Vector3.zero;
    Vector3 endPosition = Vector3.zero;

    bool isBeginDown = false;

    private bool isDraw = false;

    void Update()
    {
        UpdataFollowNiu();

        if (isDraw)
        {
            if (!isBeginDown)
            {
                isBeginDown = true;
                SaveTexture();
            }

            DrawMove(new Vector3(GetPositionInfo().x, GetPositionInfo().y, 0));

        }
        else 
        {
            isBeginDown = false;
            OnReset();
        }
        DrawImage();
    }

    private void SaveTexture()
    {
        RenderTexture newRT = new RenderTexture(mainRender);
        Graphics.Blit(mainRender, newRT);
        cacheList.Push(newRT);
    }
    private void BackDraw()
    {
        if (cacheList.Count > 0)
        {
            mainRender.Release();
            mainRender = cacheList.Pop();
        }
    }

    //更新位置
    private void UpdataFollowNiu()
    {
        if (!isTarget) return;

        Vector3 p = currentPen.nib.position;
        minImg.position = new Vector3(p.x, transform.position.y, p.z);
    }


    private void OnReset()
    {
        startPosition = Vector3.zero;
        pointCout = 0;
        speedCout = 0;
    }

    [HideInInspector]
    public float sv;

    private float SetScale(float distance)
    {
        float Scale = 0;
        if (distance < 100)
        {
            Scale = 0.8f - 0.005f * distance;
        }
        else
        {
            Scale = 0.425f - 0.00125f * distance;
        }
        if (Scale <= 0.05)//0.05f
        {
            Scale = 0.05f;//0.05f
        }
        sv = Scale * brushOutSize;
        return sv;
    }

    //传输位置坐标画点
    private void DrawMove(Vector3 pos)
    {
        if (startPosition == Vector3.zero)
        {
            startPosition = new Vector3(GetPositionInfo().x, GetPositionInfo().y, 0);
        }

        endPosition = pos;
        float distance = Vector3.Distance(startPosition, endPosition);
        brushScale = SetScale(distance);
        BezierDraw(pos, distance, 4.5f);
        startPosition = endPosition;
    }


    private void Clear()
    {
        Graphics.SetRenderTarget(mainRender);
        GL.PushMatrix();
        GL.Clear(true, true, Color.white);
        GL.PopMatrix();
    }

    public void ClearRender()
    {
        Graphics.SetRenderTarget(mainRender);

        mat.SetPass(0);

        GL.PushMatrix();//压栈
        GL.LoadOrtho();//绘制2d图 

        mat.SetTexture("_MainTex", brushPNGTexture);
        mat.SetColor("_Color",UnityEngine.Color.white);
        GL.Begin(GL.QUADS);

        GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(0, 0, 0);

        GL.End();
        GL.PopMatrix();

    }

    private void DrawBrush(RenderTexture rtTexture, int x, int y, Texture sourceTexture, Color color, float scale)
    {
        DrawBrush(rtTexture, new Rect(x, y, sourceTexture.width, sourceTexture.height), sourceTexture, color, scale);
    }
    private void DrawBrush(RenderTexture rtTexture, Rect rect, Texture sourceTexture, Color color, float scale)
    {

        float L = rect.xMin - rect.width * scale / 2.0f;
        float R = rect.xMin + rect.width * scale / 2.0f;
        float T = rect.yMin - rect.height * scale / 2.0f;
        float B = rect.yMin + rect.height * scale / 2.0f;

        Graphics.SetRenderTarget(rtTexture);

        mat.SetPass(0);

        GL.PushMatrix();//压栈
        GL.LoadOrtho();//绘制2d图 

        mat.SetTexture("_MainTex", brushPNGTexture);
        mat.SetColor("_Color", color);

        GL.Begin(GL.QUADS);

        GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(L / rawWidth, T / rawHeight, 0);
        GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(R / rawWidth, T / rawHeight, 0);
        GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(R / rawWidth, B / rawHeight, 0);
        GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(L / rawWidth, B / rawHeight, 0);

        GL.End();
        GL.PopMatrix();
    }
    private void DrawImage()
    {
        raw.texture = mainRender;
    }

    private void ClearDraw()
    {
        Clear();
        cacheList.Clear();

        
    }

    //点之间用贝塞尔加点, a1*ax+a2*bx+a3*cx+a4*dx;
    private void BezierDraw(Vector3 pos, float distance, float targetPosOffset)
    {

        PositionArray[pointCout] = pos;
        pointCout++;
        speedArray[speedCout] = distance;
        speedCout++;
        if (pointCout == 4)
        {
            Vector3 tmp1 = PositionArray[1];
            Vector3 tmp2 = PositionArray[2];

            Vector3 middle = (PositionArray[0] + PositionArray[2]) / 2;
            PositionArray[1] = (PositionArray[1] - middle) * 1.5f + middle;
            middle = (tmp1 + PositionArray[3]) / 2;
            PositionArray[2] = (PositionArray[2] - middle) * 2.1f + middle;

            for (int index1 = 0; index1 < 50 / 1.5f; index1++)
            {
                float t = (1.0f / 50) * index1;
                float a1 = Mathf.Pow(1 - t, 3);
                float a2 = Mathf.Pow(1 - t, 2) * 3 * t;
                float a3 = 3 * t * t * (1 - t);
                float a4 = t * t * t;
                //a1*ax+a2*bx+a3*cx+a4*dx;
                Vector3 p = a1 * PositionArray[0] + a2 * PositionArray[1] + a3 * PositionArray[2] + a4 * PositionArray[3];

                float deltaspeed = (float)(speedArray[3] - speedArray[0]) / 50;

                float randomOffset = 0;

                if (isBurrs)
                    randomOffset = Random.Range(-targetPosOffset, targetPosOffset);
               
                DrawBrush(mainRender, (int)(p.x + randomOffset), (int)(p.y + randomOffset), brushPNGTexture, brushCurrentColor, SetScale(speedArray[0] + (deltaspeed * index1)));
             
            }

            PositionArray[0] = tmp1;
            PositionArray[1] = tmp2;
            PositionArray[2] = PositionArray[3];

            speedArray[0] = speedArray[1];
            speedArray[1] = speedArray[2];
            speedArray[2] = speedArray[3];
            pointCout = 3;
            speedCout = 3;
        }
        else
        {
            DrawBrush(mainRender, (int)endPosition.x, (int)endPosition.y, brushPNGTexture, brushCurrentColor, brushScale);
        }

    }
    #endregion

}




