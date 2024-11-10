using PubSub;
using UnityEngine;
using Kinect4Azure;

public class KinectController_WithDuplicatedReality : KinectSubscriber
{

    [Header("Duplicated Reality")]
    public Transform RegionOfInterest;
    public Transform DuplicatedReality;

    public override void OnSetPointcloudProperties(Material pointcloudMat)
    {
        pointcloudMat.SetMatrix("_Roi2Dupl", DuplicatedReality.localToWorldMatrix * RegionOfInterest.worldToLocalMatrix);
        pointcloudMat.SetMatrix("_ROI_Inversed", RegionOfInterest.worldToLocalMatrix);
        pointcloudMat.SetMatrix("_Dupl_Inversed", DuplicatedReality.worldToLocalMatrix);
    }
}