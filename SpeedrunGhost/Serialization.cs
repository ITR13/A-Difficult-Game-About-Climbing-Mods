using System;
using System.IO;
using UnityEngine;

namespace SpeedrunningTools;

public static class Serialization
{
    public static void Serialize(BinaryWriter writer, KeyframeData keyframeData)
    {
        // prefix
        writer.Write('D');
        writer.Write('C');
        writer.Write('G');
        writer.Write(keyframeData.Version);

        writer.Write(keyframeData.Paths);
        writer.Write(keyframeData.Keyframes);
    }

    public static KeyframeData Deserialize(BinaryReader reader)
    {
        // Read and validate prefix
        var prefix1 = reader.ReadChar();
        var prefix2 = reader.ReadChar();
        var prefix3 = reader.ReadChar();
        var version = reader.ReadString();

        if (prefix1 != 'D' || prefix2 != 'C' || prefix3 != 'G')
        {
            throw new Exception("Invalid data format or version mismatch.");
        }

        var paths = reader.ReadStringArray();
        var keyframes = reader.ReadKeyframeArray();

        return new KeyframeData
        {
            Version = version,
            Paths = paths,
            Keyframes = keyframes,
        };
    }


    #region Serialization

    private static void Write(this BinaryWriter writer, string[] keyframes) => writer.Write(
        keyframes,
        (binaryWriter, s) => binaryWriter.Write(s)
    );

    private static void Write(this BinaryWriter writer, Keyframe[] keyframes) => writer.Write(keyframes, Write);
    private static void Write(this BinaryWriter writer, PositionKeyframe[] keyframes) => writer.Write(keyframes, Write);
    private static void Write(this BinaryWriter writer, RotationKeyframe[] keyframes) => writer.Write(keyframes, Write);

    private static void Write<T>(this BinaryWriter writer, T[] list, Action<BinaryWriter, T> write)
    {
        writer.Write(list.Length);
        for (var i = 0; i < list.Length; i++)
        {
            write(writer, list[i]);
        }
    }

    private static void Write(this BinaryWriter writer, Keyframe keyframe)
    {
        writer.Write(keyframe.Time);
        writer.Write(keyframe.SyncFrame);
        writer.Write(keyframe.BodyPosition);
        writer.Write(keyframe.BodyRotation);
        writer.Write(keyframe.Positions);
        writer.Write(keyframe.Rotations);
    }

    private static void Write(this BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.x);
        writer.Write(vector.y);
        writer.Write(vector.z);
    }

    private static void Write(this BinaryWriter writer, Quaternion quaternion)
    {
        writer.Write(quaternion.x);
        writer.Write(quaternion.y);
        writer.Write(quaternion.z);
        writer.Write(quaternion.w);
    }

    private static void Write(this BinaryWriter writer, PositionKeyframe keyframe)
    {
        writer.Write(keyframe.Index);
        writer.Write(keyframe.Position);
    }

    private static void Write(this BinaryWriter writer, RotationKeyframe keyframe)
    {
        writer.Write(keyframe.Index);
        writer.Write(keyframe.Rotation);
    }

    #endregion

    #region Deserialization

    private static string[] ReadStringArray(this BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var array = new string[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadString();
        }

        return array;
    }

    private static Keyframe[] ReadKeyframeArray(this BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var array = new Keyframe[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadKeyframe();
        }

        return array;
    }

    private static Keyframe ReadKeyframe(this BinaryReader reader)
    {
        var time = reader.ReadSingle();
        var syncFrame = reader.ReadBoolean();
        var bodyPosition = reader.ReadVector3();
        var bodyRotation = reader.ReadQuaternion();
        var positions = reader.ReadPositionKeyframeArray();
        var rotations = reader.ReadRotationKeyframeArray();
        return new Keyframe
        {
            Time = time, SyncFrame = syncFrame, BodyPosition = bodyPosition, BodyRotation = bodyRotation,
            Positions = positions, Rotations = rotations,
        };
    }

    private static Vector3 ReadVector3(this BinaryReader reader)
    {
        var x = reader.ReadSingle();
        var y = reader.ReadSingle();
        var z = reader.ReadSingle();
        return new Vector3(x, y, z);
    }

    private static Quaternion ReadQuaternion(this BinaryReader reader)
    {
        var x = reader.ReadSingle();
        var y = reader.ReadSingle();
        var z = reader.ReadSingle();
        var w = reader.ReadSingle();
        return new Quaternion(x, y, z, w);
    }

    private static PositionKeyframe[] ReadPositionKeyframeArray(this BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var array = new PositionKeyframe[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadPositionKeyframe();
        }

        return array;
    }

    private static PositionKeyframe ReadPositionKeyframe(this BinaryReader reader)
    {
        var index = reader.ReadByte();
        var position = reader.ReadVector3();
        return new PositionKeyframe
        {
            Index = index,
            Position = position,
        };
    }

    private static RotationKeyframe[] ReadRotationKeyframeArray(this BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var array = new RotationKeyframe[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadRotationKeyframe();
        }

        return array;
    }

    private static RotationKeyframe ReadRotationKeyframe(this BinaryReader reader)
    {
        var index = reader.ReadByte();
        var rotation = reader.ReadQuaternion();
        return new RotationKeyframe
        {
            Index = index,
            Rotation = rotation,
        };
    }

    #endregion
}