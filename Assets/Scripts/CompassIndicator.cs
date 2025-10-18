using UnityEngine;
using UnityEngine.UI;

public class CompassIndicator : MonoBehaviour
{
    public RectTransform needlePivot; // rotate this
    public RectTransform needle;      // child image
    public Text angleLabel;           // optional
    public float ringRotationOffset = 0f;

    void Update()
    {
        var hs = HeadingService.Instance;
        if (!hs || !needlePivot) return;

        float heading = hs.GetHeading() + ringRotationOffset;
        needlePivot.localEulerAngles = new Vector3(0, 0, -heading);
        if (angleLabel) angleLabel.text = Mathf.RoundToInt(heading % 360f).ToString("0°");
    }
}