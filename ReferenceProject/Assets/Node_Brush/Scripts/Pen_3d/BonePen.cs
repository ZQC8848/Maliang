using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//根据笔的位置方向，修改骨骼动画
public class BonePen : PenBase
{
    public Animator BoneAnim;
    float timer;
    Vector3 newDir;
    Vector3 firstPosition;
    Vector3 dir;
    [Range(0, 1)]
    public float boneWeight = 0.92f;
    void Update()
    {
        ChangeBurshSize();
        ListenActiveSelf();
    }

    private void LateUpdate()
    {
        if (BoneAnim) 
        {
            if (isDraw)
            {
                timer += Time.deltaTime;
                if (timer > 0.02f) //太快的间隔会让笔头看着不连贯
                {
                    timer = 0;

                    newDir = (firstPosition - transform.position);

                    newDir.Normalize();

                    dir = newDir * (1 - boneWeight) + dir * boneWeight;

                    BoneAnim.SetFloat("Y", dir.z);
                    BoneAnim.SetFloat("X", -dir.x);

                    firstPosition = transform.position;
                }
            }
            else 
            {
                BoneAnim.SetFloat("X", 0f);
                BoneAnim.SetFloat("Y", 0f);
                dir = Vector3.zero;
                newDir = Vector3.zero;
            }
        }
    }

   
}
