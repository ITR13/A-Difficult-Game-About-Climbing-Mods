using System.Collections.Generic;
using UnityEngine;

namespace SpeedrunningTools;

public class Recorder
{
    public Transform Root;
    public Transform[] Transforms;
    public Vector3[] Positions;
    public Quaternion[] Rotations;
    public List<Keyframe> Keyframes = new();

    public Recorder(Transform root, Transform[] transforms)
    {
        Root = root;
        Transforms = transforms;
        Positions = new Vector3[transforms.Length];
        Rotations = new Quaternion[transforms.Length];
        RecordKeyframe(0f, true);
    }

    public void RecordKeyframe(float timestamp, bool syncFrame)
    {
        var positionsChanged = new List<PositionKeyframe>();
        var rotationsChanged = new List<RotationKeyframe>();
        for (var i = 0; i < Transforms.Length; i++)
        {
            var position = Transforms[i].localPosition;
            var rotation = Transforms[i].localRotation;
            if (Positions[i] != position || syncFrame)
            {
                Positions[i] = position;
                positionsChanged.Add(
                    new PositionKeyframe
                    {
                        Index = (byte)i,
                        Position = position,
                    }
                );
            }

            if (Rotations[i] != rotation || syncFrame)
            {
                Rotations[i] = rotation;
                rotationsChanged.Add(
                    new RotationKeyframe()
                    {
                        Index = (byte)i,
                        Rotation = rotation,
                    }
                );
            }
        }

        Keyframes.Add(
            new Keyframe
            {
                Time = timestamp,
                SyncFrame = syncFrame,
                BodyPosition = Root.position,
                BodyRotation = Root.rotation,
                Positions = positionsChanged.ToArray(),
                Rotations = rotationsChanged.ToArray(),
            }
        );
    }

    public void PurgeAfter(float time)
    {
        for (var i = Keyframes.Count - 1; i >= 0; i--)
        {
            if (Keyframes[i].Time > time) continue;
            Keyframes.RemoveRange(i + 1, Keyframes.Count - i - 1);
            return;
        }
    }
}