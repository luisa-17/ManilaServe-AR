using UnityEngine;
using UnityEngine.UI;

public class CompassGuideUI : MonoBehaviour
{
    [Header("UI")]
    public RectTransform arrow;      // a UI arrow sprite; pivot center works fine
    public Image arrowImage;         // same object’s Image for color change
    public Text label;               // optional text: "Turn left 30°"

    [Header("Settings")]
    public float alignTolerance = 15f;   // degrees to consider "aligned"
    public Color alignedColor = Color.green;
    public Color misalignedColor = new Color(1f, 0.35f, 0.2f);

    // Target to face (either set Transform or raw position)
    Transform targetT;
    Vector3 targetPos;
    bool useTransform;

    public void SetTargetTransform(Transform t)
    {
        targetT = t; useTransform = true;
    }

    public void SetTargetPosition(Vector3 p)
    {
        targetPos = p; useTransform = false;
    }

    void Update()
    {
        var hs = HeadingService.Instance;
        var cam = Camera.main ? Camera.main.transform : null;
        if (hs == null || cam == null || (targetT == null && !useTransform)) return;

        Vector3 to = useTransform ? targetT.position : targetPos;
        float userHeading = hs.GetHeading(); // 0..360 after offset
        float routeBearing = BearingUtil.WorldBearingDeg(cam.position, to);

        float delta = Mathf.DeltaAngle(userHeading, routeBearing); // -180..+180
        // Rotate arrow so 0° means arrow points up when aligned
        // Positive delta means target is to the left (CCW), negative to the right (CW)
        if (arrow)
            arrow.localEulerAngles = new Vector3(0, 0, -delta);

        bool aligned = Mathf.Abs(delta) <= alignTolerance;

        if (arrowImage) arrowImage.color = aligned ? alignedColor : misalignedColor;

        if (label)
        {
            if (aligned) label.text = "Aligned";
            else label.text = delta > 0 ? $"Turn left {Mathf.Abs(Mathf.RoundToInt(delta))}°"
                                        : $"Turn right {Mathf.Abs(Mathf.RoundToInt(delta))}°";
        }
    }
}