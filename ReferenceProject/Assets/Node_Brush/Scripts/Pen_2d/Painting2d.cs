using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;
 
public class Painting2d : MonoBehaviour
{
 
    private RenderTexture texRender;
    public Material mat;
    public Texture brushTypeTexture;   //画笔纹理，半透明
    private float brushScale = 0.5f;
    public Color brushColor = Color.black;
    public RawImage raw;                   
    private Vector3[] PositionArray = new Vector3[4];
    private int b = 0;
    private float[] speedArray = new float[4];
    private int s = 0;
    [SerializeField]
    private int num = 50;//画的两点之间插件点的个数

    public float widthPower = 0.5f;//关联粗细
 
    Vector2 rawMousePosition;//raw图片的左下角对应鼠标位置
    float rawWidth;
    float rawHeight;
    [SerializeField]
    private const int maxCancleStep = 10;  //最大撤销的步骤（越大越耗费内存）
    [SerializeField]
    private Stack<RenderTexture> savedList = new Stack<RenderTexture>(maxCancleStep);
    [HideInInspector]
    public bool isMouseDown = false;//鼠标按下
    [HideInInspector]
    public bool isMouseEnter = false;//鼠标进入画布
    void Start()
    {
 
        //raw图片鼠标位置，宽度计算
        rawWidth = raw.rectTransform.sizeDelta.x;
        rawHeight = raw.rectTransform.sizeDelta.y;
        Vector2 rawanchorPositon = new Vector2(raw.rectTransform.anchoredPosition.x - raw.rectTransform.sizeDelta.x / 2.0f, raw.rectTransform.anchoredPosition.y - raw.rectTransform.sizeDelta.y / 2.0f);
        //计算Canvas位置偏差
		Canvas canvas=raw.canvas;
		Vector2 canvasOffset=RectTransformUtility.WorldToScreenPoint(Camera.main,canvas.transform.position) - canvas.GetComponent<RectTransform>().sizeDelta/2;
		//最终鼠标相对画布的位置
		rawMousePosition = rawanchorPositon + new Vector2(Screen.width / 2.0f, Screen.height / 2.0f) + canvasOffset;
 
        texRender = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
        Clear(texRender);
    }
 
    Vector3 startPosition = Vector3.zero;
    Vector3 endPosition = Vector3.zero;
    
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (isMouseEnter)
                SaveTexture();
        }
        if (Input.GetMouseButton(0))
        {
            isMouseDown = true;

            if(isMouseEnter)
            OnMouseMove(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0));
        }
        if (Input.GetMouseButtonUp(0))
        {
            isMouseDown = false;
            OnMouseUp();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            CanclePaint();
        }


        DrawImage();
    }

    [SerializeField] private RawImage saveImage;
    void SaveTexture()
    {
        RenderTexture newRenderTexture = new RenderTexture(texRender);
        Graphics.Blit(texRender,newRenderTexture);
        savedList.Push(newRenderTexture);
    }

   public void CanclePaint()
    {
        print(savedList.Count);
        if (savedList.Count > 0)
        {
            texRender.Release();
            texRender = savedList.Pop();
        }
    }

    void OnMouseUp()
    {
        startPosition = Vector3.zero;
        b = 0;
        s = 0;
    }
    //设置画笔宽度
    float SetScale(float distance)
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
        if (Scale <= 0.05f)
        {
            Scale = 0.05f;
        }
        return Scale * widthPower;
    }
 
    void OnMouseMove(Vector3 pos)
    {
        if (startPosition == Vector3.zero)
        {
            startPosition = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0);
        }
 
        endPosition = pos;
        float distance = Vector3.Distance(startPosition, endPosition);
        brushScale = SetScale(distance);
        ThreeOrderBézierCurse(pos, distance, 4.5f);
 
        startPosition = endPosition;
    }
 
    void Clear(RenderTexture destTexture)
    {
        Graphics.SetRenderTarget(destTexture);
        GL.PushMatrix();
        GL.Clear(true, true, Color.white);
        GL.PopMatrix();
    }
 
    void DrawBrush(RenderTexture destTexture, int x, int y, Texture sourceTexture, Color color, float scale)
    {
        DrawBrush(destTexture, new Rect(x, y, sourceTexture.width, sourceTexture.height), sourceTexture, color, scale);
    }
    void DrawBrush(RenderTexture destTexture, Rect destRect, Texture sourceTexture, Color color, float scale)
    {
 
        //增加鼠标位置根据raw图片位置换算。
        float left = (destRect.xMin-rawMousePosition.x)*Screen.width/rawWidth - destRect.width * scale / 2.0f;
        float right = (destRect.xMin - rawMousePosition.x) * Screen.width / rawWidth + destRect.width * scale / 2.0f;
        float top = (destRect.yMin - rawMousePosition.y) *Screen.height / rawHeight - destRect.height * scale / 2.0f;
        float bottom = (destRect.yMin - rawMousePosition.y) * Screen.height / rawHeight + destRect.height * scale / 2.0f;
 
        Graphics.SetRenderTarget(destTexture);

        GL.PushMatrix();
        GL.LoadOrtho();
 
        mat.SetTexture("_MainTex", brushTypeTexture);
        mat.SetColor("_Color", color);
        mat.SetPass(0);
 
        GL.Begin(GL.QUADS);
 
        GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(left / Screen.width, top / Screen.height, 0);
        GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(right / Screen.width, top / Screen.height, 0);
        GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(right / Screen.width, bottom / Screen.height, 0);
        GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(left / Screen.width, bottom / Screen.height, 0);
 
        GL.End();
        GL.PopMatrix();
    }
    bool bshow = true;
    void DrawImage()
    {
        raw.texture = texRender;
    }
    public void OnClickClear()
    {
        Clear(texRender);
        savedList.Clear();
    }
 

    //三阶贝塞尔曲线
    private void ThreeOrderBézierCurse(Vector3 pos, float distance, float targetPosOffset)
    {
        //记录坐标
        PositionArray[b] = pos;
        b++;
        //记录速度
        speedArray[s] = distance;
        s++;
        if (b == 4)
        {
            Vector3 temp1 = PositionArray[1];
            Vector3 temp2 = PositionArray[2];
 
            //修改中间两点坐标
            Vector3 middle = (PositionArray[0] + PositionArray[2]) / 2;
            PositionArray[1] = (PositionArray[1] - middle) * 1.5f + middle;
            middle = (temp1 + PositionArray[3]) / 2;
            PositionArray[2] = (PositionArray[2] - middle) * 2.1f + middle;
 
            for (int index1 = 0; index1 < num / 1.5f; index1++)
            {
                float t1 = (1.0f / num) * index1;
                Vector3 target = Mathf.Pow(1 - t1, 3) * PositionArray[0] +
                                 3 * PositionArray[1] * t1 * Mathf.Pow(1 - t1, 2) +
                                 3 * PositionArray[2] * t1 * t1 * (1 - t1) + PositionArray[3] * Mathf.Pow(t1, 3);
    
                float deltaspeed = (float)(speedArray[3] - speedArray[0]) / num;
               
                //模拟毛刺效果
                float randomOffset = Random.Range(-targetPosOffset, targetPosOffset);
                DrawBrush(texRender, (int)(target.x + randomOffset), (int)(target.y + randomOffset), brushTypeTexture, brushColor, SetScale(speedArray[0] + (deltaspeed * index1)));
            }
 
            PositionArray[0] = temp1;
            PositionArray[1] = temp2;
            PositionArray[2] = PositionArray[3];
 
            speedArray[0] = speedArray[1];
            speedArray[1] = speedArray[2];
            speedArray[2] = speedArray[3];
            b = 3;
            s = 3;
        }
        else
        {
            DrawBrush(texRender, (int)endPosition.x, (int)endPosition.y, brushTypeTexture,
                brushColor, brushScale);
        }
 
    }
}
