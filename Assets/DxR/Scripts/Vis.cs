using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using System.IO;

namespace DxR
{
    /// <summary>
    /// This is the component that needs to be attached to a GameObject (root) in order 
    /// to create a data-driven scene. This component takes in a json file, parses its
    /// encoded specification and generates scene parameters for one or more scenes.
    /// A scene is defined as a visualization with ONE type of mark.  
    /// Each scene gets its own scene root (sceneRoot) GameObject that gets created 
    /// under the root GameObject on which this component is attached to.
     /// </summary>
    public class Vis : MonoBehaviour
    {
        private bool verbose = true;
        public static string UNDEFINED = "undefined";
        public static float SIZE_UNIT_SCALE_FACTOR = 1.0f / 1000.0f;    // Each unit in the specs is 1 mm.
        public static float DEFAULT_VIS_DIMS = 500.0f;

        public string dataRootPath = "Assets/StreamingAssets/DxRData/";
        public string marksRootPath = "Assets/DxR/Resources/Marks/";
        public string visSpecsURL = "DxRSpecs/example.json";
        public JSONNode visSpecs;           // This is synced w/ GUI and text editor.
        public JSONNode visSpecsInferred;   // This is synced w/ actual vis and used for construction.

        public bool enableGUI = true;
        GameObject guiObject = null;
        GUI gui = null;

        string title;        // Title of scene displayed.
        float width;         // Width of scene in millimeters.
        float height;        // Heigh of scene in millimeters.
        float depth;         // Depth of scene in millimeters.

        public Data data;    // Data object.
        string markType;     // Type or name of mark used in scene.
        
        private GameObject sceneRoot = null;
        private GameObject markPrefab = null;
        private List<ChannelEncoding> channelEncodings = null;

        private GameObject tooltipInstance = null;

        private bool distanceVisibility = true;
        private bool gazeVisibility = true;
        private bool currentVisibility = true;

        void Start()
        {
            sceneRoot = gameObject;

            UpdateSpecs();
        }

        private void UpdateSpecs()
        {
            DeleteAll();

            Parse(visSpecsURL, out visSpecs);

            Initialize(ref visSpecs);

            Infer(data, ref visSpecs);

            Construct(visSpecs, ref sceneRoot);
        }

        private void DeleteAll()
        {
            foreach (Transform child in sceneRoot.transform)
            {
                if(child.tag != "Anchor" && child.tag != "DxRGUI")
                {
                    GameObject.Destroy(child.gameObject);
                }
            }
        }

        // Parse (JSON spec file (data file info in specs) -> expanded raw JSON specs): 
        // Read in the specs and data files to create expanded raw JSON specs.
        // Filenames should be relative to Assets/StreamingAssets/ directory.
        private void Parse(string visSpecsURL, out JSONNode visSpecs)
        {
            Parser parser = new Parser();
            parser.Parse(visSpecsURL, out visSpecs);
        }

        // Create initial objects that are required for inferrence.
        // The visSpecs should provide minimum required specs.
        private void Initialize(ref JSONNode visSpecs)
        {
            InitGUI();

            InferSceneObjectProperties(ref visSpecs);

            UpdateSceneObjectProperties(visSpecs);

            CreateDataObjectFromValues(visSpecs["data"]["values"], out data);

            CreateTooltipObject(out tooltipInstance, ref sceneRoot);

            CreateMarkObject(visSpecs["mark"].Value.ToString(), out markPrefab);
        }

        private void InitGUI()
        {
            Transform guiTransform = sceneRoot.transform.Find("DxRGUI");
            guiObject = guiTransform.gameObject;
            gui = guiObject.GetComponent<GUI>();
            gui.Init();

            if (!enableGUI)
            {
                if (guiObject != null)
                {
                    guiObject.SetActive(false);
                }
            } else {

                GUIUpdateDataList();
                GUIUpdateMarksList();

                GUIUpdateGUISpecsFromTextSpecs();
            }
        }

        private void GUIUpdateGUISpecsFromTextSpecs()
        {
            JSONNode specsFromText = JSON.Parse(Parser.GetStringFromFile(visSpecsURL));

            gui.UpdateDataValue(Path.GetFileName(specsFromText["data"]["url"].Value.ToString()));
            gui.UpdateMarkValue(specsFromText["mark"].Value.ToString());
        }

        private void GUIUpdateDataList()
        {
            string[] dirs = Directory.GetFiles(dataRootPath);
            List<string> dataList = new List<string>();
            dataList.Add("inline");
            for (int i = 0; i < dirs.Length; i++)
            {
                if(Path.GetExtension(dirs[i]) != ".meta")
                {
                    dataList.Add(Path.GetFileName(dirs[i]));
                }
            }
            gui.UpdateDataList(dataList);
        }

        private void GUIUpdateMarksList()
        {
            string[] dirs = Directory.GetDirectories(marksRootPath);
            List<string> marksList = new List<string>();
            marksList.Add("none");
            for(int i =0; i < dirs.Length; i++)
            {
                marksList.Add(Path.GetFileName(dirs[i]));
            }
            gui.UpdateMarksList(marksList);
        }

        private void InferSceneObjectProperties(ref JSONNode visSpecs)
        {
            if (visSpecs["width"] == null)
            {
                visSpecs.Add("width", new JSONNumber(DEFAULT_VIS_DIMS));
            }

            if (visSpecs["height"] == null)
            {
                visSpecs.Add("height", new JSONNumber(DEFAULT_VIS_DIMS));
            }

            if (visSpecs["depth"] == null)
            {
                visSpecs.Add("depth", new JSONNumber(DEFAULT_VIS_DIMS));
            }
        }

        public void UpdateVisFromTextSpecs()
        {
            GUIUpdateGUISpecsFromTextSpecs();
            UpdateSpecs();
        }
        
        public void UpdateVisFromGUISpecs()
        {
            Debug.Log("Update vis from GUI");
            UpdateTextSpecsFromGUISpecs();
            UpdateSpecs();
        }

        private void UpdateTextSpecsFromGUISpecs()
        {
            JSONNode specsFromText = JSON.Parse(Parser.GetStringFromFile(visSpecsURL));

            specsFromText["data"]["url"] = "DxRData/" + gui.GetCurrentDataValue();

            specsFromText["mark"] = gui.GetCurrentMarkValue();
            
            System.IO.File.WriteAllText(Path.Combine(Application.streamingAssetsPath, visSpecsURL), specsFromText.ToString(2));
        }

        // Infer (raw JSON specs -> full JSON specs): 
        // automatically fill in missing specs by inferrence (informed by marks and data type).
        private void Infer(Data data, ref JSONNode visSpecs)
        {
            InferAnchorProperties(ref visSpecs);

            if (markPrefab != null)
            {
                markPrefab.GetComponent<Mark>().Infer(data, ref visSpecs, visSpecsURL);
            } else
            {
                throw new Exception("Cannot perform inferrence without mark prefab loaded.");
            }

            // Update properties if needed - some properties, e.g., width, height, depth
            // may get changed based on inferrence.
            UpdateSceneObjectProperties(visSpecs);
        }

        private void InferAnchorProperties(ref JSONNode visSpecs)
        {
            JSONNode anchorSpecs = visSpecs["anchor"];
            if (anchorSpecs != null && anchorSpecs.Value.ToString() == "none") return;
            JSONObject anchorSpecsObj = (anchorSpecs == null) ? new JSONObject() : anchorSpecs.AsObject;
            
            if (anchorSpecsObj["placement"] == null)
            {
                anchorSpecsObj.Add("placement", new JSONString("tapToPlace"));
            }

            if(anchorSpecsObj["visibility"] == null)
            {
                anchorSpecsObj.Add("visibility", new JSONString("always"));
            }

            visSpecs.Add("anchor", anchorSpecsObj);
        }

        // Construct (full JSON specs -> working Vis): 
        private void Construct(JSONNode visSpecs, ref GameObject sceneRoot)
        {
            CreateChannelEncodingObjects(visSpecs, out channelEncodings);

            ConstructMarks(sceneRoot);

            ConstructAxes(visSpecs, ref channelEncodings, ref sceneRoot);

            ConstructLegends(visSpecs, ref channelEncodings, ref sceneRoot);

            ConstructAnchor(visSpecs, ref sceneRoot);

            ConstructPortals(visSpecs, ref sceneRoot);
        }

        private void ConstructPortals(JSONNode visSpecs, ref GameObject sceneRoot)
        {
            if (visSpecs["portals"] == null) return;

            JSONArray values = (visSpecs["portals"]["values"] == null) ? new JSONArray() :
                visSpecs["portals"]["values"].AsArray;

            if(visSpecs["portals"]["scheme"] != null)
            {
                // TODO: Load scheme contents (in local file Assets/DxR/Resources/PortalSchemes/ into values array.
            }

            GameObject portalPrefab = Resources.Load("Portal/Portal", typeof(GameObject)) as GameObject;
            if (portalPrefab == null)
            {
                throw new Exception("Cannot load Portal prefab from Assets/DxR/Resources/Portal/Portal.prefab");
            }
            else if (verbose)
            {
                Debug.Log("Loaded portal prefab");
            }

            foreach (JSONNode portalSpec in values)
            {
                Debug.Log("Portal spec: " + portalSpec.ToString());

                ConstructPortal(portalSpec, portalPrefab, ref sceneRoot);
            }
        }

        private void ConstructPortal(JSONNode portalSpec, GameObject portalPrefab, ref GameObject parent)
        {
            GameObject portalInstance = Instantiate(portalPrefab, parent.transform.position,
                        parent.transform.rotation, parent.transform);

            Vector3 localPos = Vector3.zero;
            Vector3 localRot = Vector3.zero;

            if(portalSpec["x"] != null)
            {
                localPos.x = portalSpec["x"].AsFloat * DxR.Vis.SIZE_UNIT_SCALE_FACTOR;
            }

            if (portalSpec["y"] != null)
            {
                localPos.y = portalSpec["y"].AsFloat * DxR.Vis.SIZE_UNIT_SCALE_FACTOR;
            }

            if (portalSpec["z"] != null)
            {
                localPos.z = portalSpec["z"].AsFloat * DxR.Vis.SIZE_UNIT_SCALE_FACTOR;
            }

            if (portalSpec["xrot"] != null)
            {
                localRot.x = portalSpec["xrot"].AsFloat;
            }

            if (portalSpec["yrot"] != null)
            {
                localRot.y = portalSpec["yrot"].AsFloat;
            }
            if (portalSpec["zrot"] != null)
            {
                localRot.z = portalSpec["zrot"].AsFloat;
            }

            portalInstance.transform.localPosition = localPos;
            portalInstance.transform.localEulerAngles = localRot;
        }

        private void ConstructAnchor(JSONNode visSpecs, ref GameObject sceneRoot)
        {
            if (visSpecs["anchor"] == null) return;

            Anchor anchor = sceneRoot.transform.GetComponentInChildren<Anchor>();
            if(anchor != null)
            {
                anchor.UpdateSpecs(visSpecs["anchor"]);
            }
        }

        private void CreateTooltipObject(out GameObject tooltipInstance, ref GameObject parent)
        {
            GameObject tooltipPrefab = Resources.Load("Tooltip/tooltip") as GameObject;
            tooltipInstance = Instantiate(tooltipPrefab, parent.transform.position,
                        parent.transform.rotation, parent.transform);

            if (tooltipInstance == null)
            {
                throw new Exception("Cannot load tooltip");
            }
            else if (verbose)
            {
                Debug.Log("Loaded tooltip");
            }

            tooltipInstance.name = "tooltip";
            tooltipInstance.GetComponent<Tooltip>().SetAnchor("upperleft");
            tooltipInstance.SetActive(false);            
        }

        private void UpdateSceneObjectProperties(JSONNode visSpecs)
        {
            if (visSpecs["title"] != null)
            {
                title = visSpecs["title"].Value;
            }

            if (visSpecs["width"] != null)
            {
                width = visSpecs["width"].AsFloat;
            } 

            if (visSpecs["height"] != null)
            {
                height = visSpecs["height"].AsFloat;
            }

            if (visSpecs["depth"] != null)
            {
                depth = visSpecs["depth"].AsFloat;
            }
        }

        private void CreateDataObjectFromValues(JSONNode valuesSpecs, out Data data)
        {
            data = new Data();

            CreateDataFields(valuesSpecs, ref data);

            data.values = new List<Dictionary<string, string>>();

            int numDataFields = data.fieldNames.Count;
            if (verbose)
            {
                Debug.Log("Counted " + numDataFields.ToString() + " fields in data.");
            }

            // Loop through the values in the specification
            // and insert one Dictionary entry in the values list for each.
            foreach (JSONNode value in valuesSpecs.Children)
            {
                Dictionary<string, string> d = new Dictionary<string, string>();

                bool valueHasNullField = false;
                for (int fieldIndex = 0; fieldIndex < numDataFields; fieldIndex++)
                {
                    string curFieldName = data.fieldNames[fieldIndex];

                    // TODO: Handle null / missing values properly.
                    if (value[curFieldName].IsNull)
                    {
                        valueHasNullField = true;
                        Debug.Log("value null found: ");
                        break;
                    }
                    /*
                    if(curFieldName == "vmeg" && value["vmeg"] == 0)
                    {
                        valueHasNullField = true;
                        Debug.Log("value null found: ");
                        break;
                    }
                    */

                    d.Add(curFieldName, value[curFieldName]);
                }

                if (!valueHasNullField)
                {
                    data.values.Add(d);
                }
            }

//            SubsampleData(valuesSpecs, 8, "Assets/DxR/Resources/cars_subsampled.json");
        }

        
        private void SubsampleData(JSONNode data, int samplingRate, string outputName)
        {
            JSONArray output = new JSONArray();
            int counter = 0;
            foreach (JSONNode value in data.Children)
            {
                if (counter % 8 == 0)
                {
                    output.Add(value);
                }
                counter++;
            }

            System.IO.File.WriteAllText(outputName, output.ToString());
        }
        

        private void CreateDataFields(JSONNode valuesSpecs, ref Data data)
        {
            data.fieldNames = new List<string>();
            foreach (KeyValuePair<string, JSONNode> kvp in valuesSpecs[0].AsObject)
            {
                data.fieldNames.Add(kvp.Key);

                if (verbose)
                {
                    Debug.Log("Reading data field: " + kvp.Key);
                }
            }
        }

        private void CreateMarkObject(string markType, out GameObject markPrefab)
        {
            string markNameLowerCase = markType.ToLower();
            markPrefab = Resources.Load("Marks/" + markNameLowerCase + "/" + markNameLowerCase) as GameObject;

            if (markPrefab == null)
            {
                throw new Exception("Cannot load mark " + markNameLowerCase);
            }
            else if (verbose)
            {
                Debug.Log("Loaded mark " + markNameLowerCase);
            }

            markPrefab.GetComponent<Mark>().markName = markNameLowerCase;
        }

        private void CreateChannelEncodingObjects(JSONNode visSpecs, out List<ChannelEncoding> channelEncodings)
        {
            channelEncodings = new List<ChannelEncoding>();

            // Go through each channel and create ChannelEncoding for each:
            foreach (KeyValuePair<string, JSONNode> kvp in visSpecs["encoding"].AsObject)
            {
                ChannelEncoding channelEncoding = new ChannelEncoding();

                channelEncoding.channel = kvp.Key;
                JSONNode channelSpecs = kvp.Value;
                if (channelSpecs["value"] != null)
                {
                    channelEncoding.value = channelSpecs["value"].Value.ToString();

                    if (channelSpecs["type"] != null)
                    {
                        channelEncoding.valueDataType = channelSpecs["type"].Value.ToString();
                    }
                }
                else
                {
                    channelEncoding.field = channelSpecs["field"];

                    if (channelSpecs["type"] != null)
                    {
                        channelEncoding.fieldDataType = channelSpecs["type"];
                    }
                    else
                    {
                        throw new Exception("Missing type for field in channel " + channelEncoding.channel);
                    }
                }

                JSONNode scaleSpecs = channelSpecs["scale"];
                if (scaleSpecs != null)
                {
                   CreateScaleObject(scaleSpecs, ref channelEncoding.scale);
                }

                channelEncodings.Add(channelEncoding);
            }
        }

        private void CreateScaleObject(JSONNode scaleSpecs, ref Scale scale)
        {
            switch (scaleSpecs["type"].Value.ToString())
            {
                case "none":
                case "custom":
                    scale = new ScaleCustom(scaleSpecs);
                    break;

                case "linear":
                    scale = new ScaleLinear(scaleSpecs);
                    break;

                case "band":
                case "point":
                    scale = new ScaleBand(scaleSpecs);
                    break;

                case "ordinal":
                    scale = new ScaleOrdinal(scaleSpecs);
                    break;

                case "sequential":
                    scale = new ScaleSequential(scaleSpecs);
                    break;

                default:
                    scale = null;
                    break;
            }
        }

        private void ConstructMarks(GameObject sceneRoot)
        {
            if(markPrefab != null)
            {
                // Create one mark prefab instance for each data point:
                foreach (Dictionary<string, string> dataValue in data.values)
                {
                    // Instantiate mark prefab
                    GameObject markInstance = InstantiateMark(markPrefab, sceneRoot.transform);

                    // Copy data in mark:
                    markInstance.GetComponent<Mark>().datum = dataValue;

                    // Apply channel encodings:
                    ApplyChannelEncoding(channelEncodings, dataValue, ref markInstance);
                }
            } else
            {
                throw new Exception("Error constructing marks with mark prefab not loaded.");
            }
        }

        private GameObject InstantiateMark(GameObject markPrefab, Transform parentTransform)
        {
            return Instantiate(markPrefab, parentTransform.position,
                        parentTransform.rotation, parentTransform);
        }

        private void ApplyChannelEncoding(List<ChannelEncoding> channelEncodings, 
            Dictionary<string, string> dataValue, ref GameObject markInstance)
        {
            Mark markComponent = markInstance.GetComponent<Mark>();
            if(markComponent == null)
            {
                throw new Exception("Mark component not present in mark prefab.");
            }

            foreach (ChannelEncoding channelEncoding in channelEncodings)
            {
                if (channelEncoding.value != DxR.Vis.UNDEFINED)
                {
                    markComponent.SetChannelValue(channelEncoding.channel, channelEncoding.value);
                }
                else
                {
                    if(channelEncoding.channel == "tooltip")
                    {
                        SetupTooltip(channelEncoding, markComponent);
                    } else
                    {
                        string channelValue = channelEncoding.scale.ApplyScale(dataValue[channelEncoding.field]);
                        markComponent.SetChannelValue(channelEncoding.channel, channelValue);
                    }
                }
            }
        }

        private void SetupTooltip(ChannelEncoding channelEncoding, Mark markComponent)
        {
            if (tooltipInstance != null)
            {
                markComponent.SetTooltipObject(ref tooltipInstance);
                markComponent.SetTooltipField(channelEncoding.field);
            }
        }

        private void ConstructAxes(JSONNode visSpecs, ref List<ChannelEncoding> channelEncodings, ref GameObject sceneRoot)
        {
            // Go through each channel and create axis for each spatial / position channel:
            for (int channelIndex = 0; channelIndex < channelEncodings.Count; channelIndex++)
            {
                ChannelEncoding channelEncoding = channelEncodings[channelIndex];
                JSONNode axisSpecs = visSpecs["encoding"][channelEncoding.channel]["axis"];
                if (axisSpecs != null && axisSpecs.Value.ToString() != "none" && 
                    (channelEncoding.channel == "x" || channelEncoding.channel == "y" || 
                    channelEncoding.channel == "z" || 
                    channelEncoding.channel == "width" || channelEncoding.channel == "height" ||
                    channelEncoding.channel == "depth"))
                {
                    if (verbose)
                    {
                        Debug.Log("Constructing axis for channel " + channelEncoding.channel);
                    }

                    ConstructAxisObject(axisSpecs, ref channelEncoding, ref sceneRoot);
                }
            }
        }

        // TODO: Move all this in axis object.
        private void ConstructAxisObject(JSONNode axisSpecs, ref ChannelEncoding channelEncoding, ref GameObject sceneRoot)
        {
            GameObject axisPrefab = Resources.Load("Axis/Axis", typeof(GameObject)) as GameObject;
            if (axisPrefab != null)
            {
                channelEncoding.axis = Instantiate(axisPrefab, sceneRoot.transform);

                // TODO: Move all the following update code to the Axis object class.

                if (axisSpecs["title"] != null)
                {
                    channelEncoding.axis.GetComponent<Axis>().SetTitle(axisSpecs["title"].Value);
                }

                if(axisSpecs["titlePadding"] != null)
                {
                    channelEncoding.axis.GetComponent<Axis>().SetTitlePadding(axisSpecs["titlePadding"].Value);
                }

                float axisLength = 0.0f;
                if (axisSpecs["length"] != null)
                {
                    axisLength = axisSpecs["length"].AsFloat;
                }
                else
                {
                    // TODO: Move this to infer stage.
                    switch (channelEncoding.channel)
                    {
                        case "x":
                        case "width":
                            axisLength = width;
                            break;
                        case "y":
                        case "height":
                            axisLength = height;
                            break;
                        case "z":
                        case "depth":
                            axisLength = depth;
                            break;
                        default:
                            axisLength = 0.0f;
                            break;
                    }
                    channelEncoding.axis.GetComponent<Axis>().SetLength(axisLength);
                }

                if(axisSpecs["orient"] != null && axisSpecs["face"] != null)
                {
                    channelEncoding.axis.GetComponent<Axis>().SetOrientation(axisSpecs["orient"].Value, axisSpecs["face"].Value);
                } else
                {
                    throw new Exception("Axis of channel " + channelEncoding.channel + " requires both orient and face specs.");
                }

                if(axisSpecs["ticks"].AsBool && axisSpecs["values"] != null)
                {
                    channelEncoding.axis.GetComponent<Axis>().ConstructTicks(axisSpecs, channelEncoding.scale);
                }

                // TODO: Do the axis color coding more elegantly.  
                // Experimental: Set color of axis based on channel type.
                channelEncoding.axis.GetComponent<Axis>().EnableAxisColorCoding(channelEncoding.channel);
            }
            else
            {
                throw new Exception("Cannot find axis prefab.");
            }
        }
        
        private void ConstructLegends(JSONNode visSpecs, ref List<ChannelEncoding> channelEncodings, ref GameObject sceneRoot)
        {
            // Go through each channel and create legend for color, shape, or size channels:
            for (int channelIndex = 0; channelIndex < channelEncodings.Count; channelIndex++)
            {
                ChannelEncoding channelEncoding = channelEncodings[channelIndex];
                JSONNode legendSpecs = visSpecs["encoding"][channelEncoding.channel]["legend"];
                if (legendSpecs != null && legendSpecs.Value.ToString() != "none")
                {
                    if (verbose)
                    {
                        Debug.Log("Constructing legend for channel " + channelEncoding.channel);
                    }

                    ConstructLegendObject(legendSpecs, ref channelEncoding, ref sceneRoot);
                }
            }
        }

        private void ConstructLegendObject(JSONNode legendSpecs, ref ChannelEncoding channelEncoding, ref GameObject sceneRoot)
        {
            GameObject legendPrefab = Resources.Load("Legend/Legend", typeof(GameObject)) as GameObject;
            if (legendPrefab != null && markPrefab != null)
            {
                channelEncoding.legend = Instantiate(legendPrefab, sceneRoot.transform);
                channelEncoding.legend.GetComponent<Legend>().UpdateSpecs(legendSpecs, ref channelEncoding, markPrefab);
            }
            else
            {
                throw new Exception("Cannot find legend prefab.");
            }
        }

        public void SetVisibility(bool val)
        {
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                var child = gameObject.transform.GetChild(i).gameObject;
                if (child != null && child.name != "Anchor")
                {
                    child.SetActive(val);
                }
            }
        }

        public void SetDistanceVisibility(bool val)
        {
            distanceVisibility = val;
            if((distanceVisibility && gazeVisibility) != currentVisibility)
            {
                currentVisibility = distanceVisibility && gazeVisibility;
                SetVisibility(currentVisibility);
            }
        }

        public void SetGazeVisibility(bool val)
        {
            gazeVisibility = val;
            if ((distanceVisibility && gazeVisibility) != currentVisibility)
            {
                currentVisibility = distanceVisibility && gazeVisibility;
                SetVisibility(currentVisibility);
            }
        }
    }
}
