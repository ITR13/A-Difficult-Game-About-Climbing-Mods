﻿using System;
using UnityEngine;

namespace SpeedrunGhost;

public class Playback
{
    public Keyframe[] Keyframes;
    public Transform Root;
    public Transform[] Transforms;

    public int CurrIndex;
    public float Time;

    public Vector3[] Positions;
    public Quaternion[] Rotations;

    public Playback(Transform root, Transform[] transforms, Keyframe[] keyframes)
    {
        Root = root;
        Transforms = transforms;
        Keyframes = keyframes;

        CurrIndex = 0;
        Time = 0;

        Positions = new Vector3[Transforms.Length];
        Rotations = new Quaternion[Transforms.Length];

        SetValues();

        if (keyframes.Length <= 1 || keyframes[1].Time <= 0)
        {
            throw new Exception("Bad keyframe data");
        }
    }

    public void Update(float deltaTime)
    {
        Time += deltaTime;
        if (Keyframes[^1].Time <= Time)
        {
            Reset();
        }
        else if (Keyframes[CurrIndex + 1].Time <= Time)
        {
            CurrIndex++;
            SetValues();
        }

        var fromFrame = Keyframes[CurrIndex];
        var toFrame = Keyframes[CurrIndex + 1];
        var lerpTime = Mathf.InverseLerp(fromFrame.Time, toFrame.Time, Time);

        Root.position = Vector3.Lerp(fromFrame.BodyPosition, toFrame.BodyPosition, lerpTime);
        Root.rotation = Quaternion.Lerp(fromFrame.BodyRotation, toFrame.BodyRotation, lerpTime);

        foreach (var positionKeyframe in toFrame.Positions)
        {
            Transforms[positionKeyframe.Index].localPosition = Vector3.Lerp(
                Positions[positionKeyframe.Index],
                positionKeyframe.Position,
                lerpTime
            );
        }

        foreach (var rotationKeyframe in toFrame.Rotations)
        {
            Transforms[rotationKeyframe.Index].localRotation = Quaternion.Lerp(
                Rotations[rotationKeyframe.Index],
                rotationKeyframe.Rotation,
                lerpTime
            );
        }
    }

    public void Reset()
    {
        CurrIndex = 0;
        Time = 0;
        SetValues();
    }

    private void SetValues()
    {
        var keyframe = Keyframes[CurrIndex];
        foreach (var positionKeyframe in keyframe.Positions)
        {
            Positions[positionKeyframe.Index] = positionKeyframe.Position;
            Transforms[positionKeyframe.Index].localPosition = positionKeyframe.Position;
        }

        foreach (var rotationKeyframe in keyframe.Rotations)
        {
            Rotations[rotationKeyframe.Index] = rotationKeyframe.Rotation;
            Transforms[rotationKeyframe.Index].localRotation = rotationKeyframe.Rotation;
        }
    }

    public void JumpTo(float time)
    {
        var length = Keyframes[^1].Time;
        while (time >= length)
        {
            time -= length;
        }

        Time = time;

        var target = -1;
        for (var i = 0; i < Keyframes.Length; i++)
        {
            if (Keyframes[i].SyncFrame)
            {
                CurrIndex = i - 1;
            }

            if (Keyframes[i].Time >= time)
            {
                target = i;
                break;
            }
        }

        for (var i = CurrIndex; i <= target; i++)
        {
            Update(0);
        }

        if (CurrIndex != target)
        {
            Debug.LogError("!§!!");
        }
    }
}