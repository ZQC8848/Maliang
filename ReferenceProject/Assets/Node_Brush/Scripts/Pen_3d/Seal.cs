using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Seal : PenBase
{
    public Texture penStyle;


    public override void Init()
    {
        base.Init();

        SetBurshStyle(penStyle);

        PaintingHandler.instance.isBurrs = false;
        isDraw = false;

        PaintingHandler.instance.ClearRender();
    }

    void Update()
    {
        ChangeBurshSize();
        ListenActiveSelf();
    }

  

}
