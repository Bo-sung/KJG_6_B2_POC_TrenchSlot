using UnityEngine;

public sealed class AmmoPickupView : MonoBehaviour
{
    private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

    [SerializeField] private Renderer bodyRenderer;
    public Vector3 CollectionPoint => bodyRenderer != null ? bodyRenderer.bounds.center : transform.position;

    public void SetColor(Color _color)
    {
        if (bodyRenderer != null)
        {
            var block = new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(block);
            block.SetColor(BASE_COLOR_ID, _color);
            bodyRenderer.SetPropertyBlock(block);
        }
    }
}
