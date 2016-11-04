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

 void Start ()
    {

        // Cache the reference to the game world's
        // camera.
        _camera = GetComponent<Camera>();

        //sets gravity for game based on phones own gravity scale
        gyro = Input.gyro;
        gravityMagnitude = Physics.gravity.magnitude *
                           gravityScale;

        // Try to find a (physical) camera that faces
        // the required direction.
        WebCamDevice[] devices = WebCamTexture.devices;
        int numDevices = devices.Length;
        for (int i = 0; i < numDevices; i++)
        {
            WebCamDevice device = devices[i];
            if (device.isFrontFacing ==
                        useFrontFacingCamera)
            {
                string name = device.name;
                Debug.Log("Selecting camera with " +
                          "index " + i + " and name " +
                          name);
                webCamTexture = new WebCamTexture(
                        name, preferredCaptureWidth,
                        preferredCaptureHeight,
                        preferredFPS);
                break;
            }
        }

        if (webCamTexture == null)
        {
            // No camera faces the required direction.
            // Give up.
            Debug.LogError("No suitable camera found");
            Destroy(this);
            return;
        }

        // Ask the camera to start capturing.
        webCamTexture.Play();

        if (gyro != null)
        {
            gyro.enabled = true;
        }

        // Wait for the camera to start capturing.
        // Then, initialize everything else.
        StartCoroutine(Init());
    }

   IEnumerator Init ()
    {

        // Wait for the camera to start capturing.
        while (!webCamTexture.didUpdateThisFrame)
        {
            yield return null;
        }

        //set parameters for max width and height of capture
        int captureWidth = webCamTexture.width;
        int captureHeight = webCamTexture.height;

        //set parameter for diagnol for improved quality
        float captureDiagonal = Mathf.Sqrt(
                captureWidth * captureWidth +
                captureHeight * captureHeight);
        Debug.Log("Started capturing frames at " +
                  captureWidth + "x" + captureHeight);

        colors = new Color32[
                captureWidth * captureHeight];

        //materials that will be used to help detect shapes
        rgbaMat = new Mat(captureHeight, captureWidth,
                          CvType.CV_8UC4);
        grayMat = new Mat(captureHeight, captureWidth,
                          CvType.CV_8UC1);

        //canny material aids in edge filtering
        cannyMat = new Mat(captureHeight, captureWidth,
                           CvType.CV_8UC1);

        transform.localPosition =
                new Vector3(0f, 0f, -captureWidth);
        _camera.nearClipPlane = 1;
        _camera.farClipPlane = captureWidth + 1;
        _camera.orthographicSize =
                0.5f * captureDiagonal;
        raycastDistance = 0.5f * captureWidth;

        Transform videoRendererTransform =
                videoRenderer.transform;
        videoRendererTransform.localPosition =
                new Vector3(captureWidth / 2,
                            -captureHeight / 2, 0f);
        videoRendererTransform.localScale =
                new Vector3(captureWidth,
                            captureHeight, 1f);

        videoRenderer.material.mainTexture =
                webCamTexture;

        // Calculate the conversion factors between
        // image and screen coordinates.
        screenWidth = (float) Screen.width;
        screenHeight = (float) Screen.height;
        screenPixelsPerImagePixel =
                screenWidth / captureHeight;
        screenPixelsYOffset =
                0.5f * (screenHeight - (screenWidth *
                captureWidth / captureHeight));

        //set arbritary line thickness for scanned lines
        lineThickness = 0.01f * screenWidth;

        //button for displayed in game
        buttonRect = new UnityEngine.Rect(
                0.4f * screenWidth,
                0.75f * screenHeight,
                0.2f * screenWidth,
                0.1f * screenHeight);

        InitBlobDetector();
    }

void InitBlobDetector ()
    {

        // Try to create the blob detector.
        blobDetector = FeatureDetector.create(
                FeatureDetector.SIMPLEBLOB);
        if (blobDetector == null)
        {
            Debug.LogError(
                    "Unable to create blob detector");
            Destroy(this);
            return;
        }

        // The blob detector parameters must be put inta a yaml file for unity
        string blobDetectorParams = @"%YAML:1.0
thresholdStep: 10.0
minThreshold: 50.0
maxThreshold: 220.0
minRepeatability: 2
minDistBetweenBlobs: 10.0
filterByColor: False
blobColor: 0
filterByArea: True
minArea: 50.0
maxArea: 5000.0
filterByCircularity: True
minCircularity: 0.8
maxCircularity: 3.4028234663852886e+38
filterByInertia: False
minInertiaRatio: 0.1
maxInertiaRatio: 3.4028234663852886e+38
filterByConvexity: False
minConvexity: 0.95
maxConvexity: 3.4028234663852886e+38
";

        // Try to write the blob detector's parameters
        // to a temporary file.
        string path = Application.persistentDataPath +
                      "/blobDetectorParams.yaml";
        File.WriteAllText(path, blobDetectorParams);
        if (!File.Exists(path))
        {
            Debug.LogError(
                    "Unable to write blob " +
                    "detector's parameters to " +
                    path);
            Destroy(this);
            return;
        }

        // Read the blob detector's parameters from the
        // temporary file.
        blobDetector.read(path);

        // Delete the temporary file.
        File.Delete(path);
    }

    void Update ()
    {

        if (rgbaMat == null)
        {
            // Initialization is not yet complete.
            return;
        }

        if (gyro != null)
        {
            //get games gravity and and convert it into real world gravity
            Vector3 gravity = gyro.gravity;
            gravity.z = 0f;
            gravity = gravityMagnitude *
                      gravity.normalized;
            Physics.gravity = gravity;
        }

        if (!webCamTexture.didUpdateThisFrame)
        {
            // No new frame is ready.
            return;
        }

        if (simulating)
        {
            // No new detection results are needed.
            return;
        }

        // Convert the RGBA image to open cv format
        Utils.webCamTextureToMat(webCamTexture,
                                 rgbaMat, colors);

        // Convert the OpenCV image to gray scale
        Imgproc.cvtColor(rgbaMat, grayMat,
                         Imgproc.COLOR_RGBA2GRAY);
        Imgproc.Canny(grayMat, cannyMat, 50.0, 200.0);
        Imgproc.equalizeHist(grayMat, grayMat);

        UpdateCircles();
        UpdateLines();
    }

void UpdateCircles ()
    {

        // Detect blobs.
        blobDetector.detect(grayMat, blobs);

   
        // Calculate the circles' screen coordinates
        // and world coordinates.

        // Clear the previous coordinates.
        circles.Clear();

        // Iterate over the blobs.
        KeyPoint[] blobsArray = blobs.toArray();
        int numBlobs = blobsArray.Length;
        for (int i = 0; i < numBlobs; i++)
        {

            // Convert blobs' image coordinates to
            // screen coordinates.
            KeyPoint blob = blobsArray[i];
            Point imagePoint = blob.pt;
            Vector2 screenPosition =
                    ConvertToScreenPosition(
                            (float) imagePoint.x,
                            (float) imagePoint.y);
            float screenDiameter =
                    blob.size *
                    screenPixelsPerImagePixel;

            // Convert screen coordinates to world
            // coordinates based on raycasting.
            Vector3 worldPosition =
                    ConvertToWorldPosition(
                            screenPosition);

            Circle circle = new Circle(
                    screenPosition, screenDiameter,
                    worldPosition);
            circles.Add(circle);
        }
    }

    void UpdateLines ()
    {

        // Detect lines.
        Imgproc.HoughLinesP(cannyMat, houghLines, 1.0,
                            Mathf.PI / 180.0, 50,
                            50.0, 10.0);

        //
        // Calculate the lines' screen coordinates and
        // world coordinates.
        //

        // Clear the previous coordinates.
        lines.Clear();

        // Iterate over the lines.
        int numHoughLines = houghLines.cols() *
                            houghLines.rows() *
                            houghLines.channels();
        int[] houghLinesArray = new int[numHoughLines];
        houghLines.get(0, 0, houghLinesArray);
        for (int i = 0; i < numHoughLines; i += 4)
        {

            // Convert lines' image coordinates to  screen coordinates.
            Vector2 screenPoint0 =
                    ConvertToScreenPosition(
                            houghLinesArray[i],
                            houghLinesArray[i + 1]);
            Vector2 screenPoint1 =
                    ConvertToScreenPosition(
                            houghLinesArray[i + 2],
                            houghLinesArray[i + 3]);

            // Convert screen coordinates to world
            // coordinates based on raycasting.
            Vector3 worldPoint0 =
                    ConvertToWorldPosition(
                            screenPoint0);
            Vector3 worldPoint1 =
                    ConvertToWorldPosition(
                            screenPoint1);

            Line line = new Line(
                    screenPoint0, screenPoint1,
                    worldPoint0, worldPoint1);
            lines.Add(line);
        }
    }

    Vector2 ConvertToScreenPosition ( float imageX,
                                    float imageY )
    {
        float screenX = screenWidth - imageY *
                        screenPixelsPerImagePixel;
        float screenY = screenHeight - imageX *
                        screenPixelsPerImagePixel -
                        screenPixelsYOffset;
        return new Vector2(screenX, screenY);
    }

    Vector3 ConvertToWorldPosition (
            Vector2 screenPosition )
    {
        Ray ray = _camera.ScreenPointToRay(
                screenPosition);
        return ray.GetPoint(raycastDistance);
    }

    void OnPostRender ()
    {
        if (!simulating)
        {
            DrawPreview();
        }
    }

        void DrawPreview ()
    {

        // Draw red markers for scanned shapes

        int numCircles = circles.Count;
        int numLines = lines.Count;
        if (numCircles < 1 && numLines < 1)
        {
            return;
        }

        GL.PushMatrix();
        if (drawPreviewMaterial != null)
        {
            drawPreviewMaterial.SetPass(0);
        }
        GL.LoadPixelMatrix();

        if (numCircles > 0)
        {
            // Draw the circles.
            //iterate through scanned circles and place red marker with openGL
            GL.Begin(GL.QUADS);
            for (int i = 0; i < numCircles; i++)
            {
                Circle circle = circles[i];
                float centerX =
                        circle.screenPosition.x;
                float centerY =
                        circle.screenPosition.y;
                float radius =
                        0.5f * circle.screenDiameter;
                float minX = centerX - radius;
                float maxX = centerX + radius;
                float minY = centerY - radius;
                float maxY = centerY + radius;
                GL.Vertex3(minX, minY, 0f);
                GL.Vertex3(minX, maxY, 0f);
                GL.Vertex3(maxX, maxY, 0f);
                GL.Vertex3(maxX, minY, 0f);
            }
            GL.End();
        }

        if (numLines > 0)
        {
            // Draw the lines.
            //iterate through scanned lines and place red marker with openGL
            GL.Begin(GL.LINES);
            for (int i = 0; i < numLines; i++)
            {
                Line line = lines[i];
                GL.Vertex(line.screenPoint0);
                GL.Vertex(line.screenPoint1);
            }
            GL.End();
        }

        GL.PopMatrix();
    }

    void OnGUI ()
    {
        //button thats present when simulated game beings
        GUI.skin.button.fontSize = buttonFontSize;
        if (simulating)
        {
            if (GUI.Button(buttonRect,
                           "Stop Simulation"))
            {
                StopSimulation();
            }
        }
        else {
            //button thats present when camera is active
            if (GUI.Button(buttonRect,
                           "Start Simulation"))
            {
                StartSimulation();
            }
        }
    }
