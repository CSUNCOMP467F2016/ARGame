using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

using OpenCVForUnity;




[RequireComponent(typeof(Camera))]
public sealed class DetectAndSimulate : MonoBehaviour
{

    //Circle representing both in camera & in game
    struct Circle
    {

        public Vector2 screenPosition; //position from camera
        public float screenDiameter;    //diameter found from blob detection from camera
        public Vector3 worldPosition; // position transormed into unity space

        public Circle ( Vector2 screenPosition,
                      float screenDiameter,
                      Vector3 worldPosition )
        {
            this.screenPosition = screenPosition;
            this.screenDiameter = screenDiameter;
            this.worldPosition = worldPosition;
        }
    }

    struct Line
    {

        public Vector2 screenPoint0; //starting point on camera
        public Vector2 screenPoint1;//ending point on camera
        public Vector3 worldPoint0; //starting point in game
        public Vector3 worldPoint1;//ending point in game

        public Line ( Vector2 screenPoint0,
                    Vector2 screenPoint1,
                    Vector3 worldPoint0,
                    Vector3 worldPoint1 )
        {
            this.screenPoint0 = screenPoint0;
            this.screenPoint1 = screenPoint1;
            this.worldPoint0 = worldPoint0;
            this.worldPoint1 = worldPoint1;
        }
    }

    [SerializeField]
    bool useFrontFacingCamera = false; //limit user to only using back camera
    [SerializeField]
    int preferredCaptureWidth = 640; 
    [SerializeField]
    int preferredCaptureHeight = 480;
    [SerializeField]
    int preferredFPS = 15;

    [SerializeField]
    Renderer videoRenderer;

    [SerializeField]
    Material drawPreviewMaterial; //red material that for "scanning" lines and circles

    [SerializeField]
    float gravityScale = 8f;

    [SerializeField]
    GameObject simulatedCirclePrefab; //prefab used to replace cirlces from camera
    [SerializeField]
    GameObject simulatedLinePrefab; //prefab used to replace lines form camera

    [SerializeField]
    int buttonFontSize = 24;

    Camera _camera;

    WebCamTexture webCamTexture;
    Color32[] colors; 
    Mat rgbaMat;
    Mat grayMat;
    Mat cannyMat;

    float screenWidth;
    float screenHeight;
    float screenPixelsPerImagePixel;
    float screenPixelsYOffset;

    float raycastDistance;
    float lineThickness;
    UnityEngine.Rect buttonRect;

    FeatureDetector blobDetector;
    MatOfKeyPoint blobs = new MatOfKeyPoint();
    List<Circle> circles = new List<Circle>(); //list of scanned circles

    Mat houghLines = new Mat();
    List<Line> lines = new List<Line>(); //list of scanned lines

    Gyroscope gyro; //gyro from mobile phone
    float gravityMagnitude;

    List<GameObject> simulatedObjects = //list of game objects instantiated
            new List<GameObject>();
    bool simulating
    {
        get
        {
            return simulatedObjects.Count > 0;
        }
    }

 