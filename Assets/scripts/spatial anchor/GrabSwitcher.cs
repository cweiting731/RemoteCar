using UnityEngine;
using Oculus.Interaction;

public class GrabSwitcher : MonoBehaviour
{
    public GrabInteractable grabInteractable;
    public RayInteractable rayGrabInteractable;

    public void Switch()
    {
        if (grabInteractable != null)
        {
            grabInteractable.enabled = !grabInteractable.enabled;
        }
        if (rayGrabInteractable != null)
        {
            rayGrabInteractable.enabled = !rayGrabInteractable.enabled;
        }
    }
}