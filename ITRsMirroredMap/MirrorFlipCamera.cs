using UnityEngine;

[RequireComponent(typeof(Camera))]
[ExecuteInEditMode]
public class MirrorFlipCamera : MonoBehaviour {
    Camera _camera;

    void Awake () {
        _camera = GetComponent<Camera>();
    }

    void OnPreCull() {
        _camera.ResetWorldToCameraMatrix();
        _camera.ResetProjectionMatrix();
        var scale = new Vector3(-1, 1, 1);
        _camera.projectionMatrix = _camera.projectionMatrix * Matrix4x4.Scale(scale);
    }

    void OnPreRender () {
        GL.invertCulling = true;
    }
	
    void OnPostRender () {
        GL.invertCulling = false;
    }
}