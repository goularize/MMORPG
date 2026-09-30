using UnityEngine;
using TMPro;

namespace Client.World
{
    public class NameTag : MonoBehaviour
    {
        public TextMeshPro textMesh;

        public void SetName(string name)
        {
            if (textMesh == null)
            {
                textMesh = GetComponentInChildren<TextMeshPro>();
            }

            if (textMesh != null)
            {
                textMesh.text = name;
            }
        }

        private void LateUpdate()
        {
            // Keeps the text perfectly upright even if the parent character rotates
            transform.rotation = Quaternion.identity;
        }
    }
}
