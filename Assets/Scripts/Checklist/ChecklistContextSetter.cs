using UnityEngine;

public class ChecklistContextSetter : MonoBehaviour
{
    public string officeId = "MHD";
    public string serviceId = "MHD1";

    void Awake()
    {
        ChecklistContext.SelectedOfficeId = officeId;
        ChecklistContext.SelectedServiceId = serviceId;
    }
}