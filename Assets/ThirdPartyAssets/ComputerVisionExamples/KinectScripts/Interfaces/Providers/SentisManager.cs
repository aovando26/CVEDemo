using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.InferenceEngine;
using UnityEngine;


namespace com.rfilkov.providers
{
    /// <summary>
    /// Sentis model runtime.
    /// </summary>
    public class RuntimeModel
    {
        public string _name;
        public int _fsIndex;
        public Model _model;
        public Worker _worker;

        public string[] inputNames;
        public string[] outputNames;

        public Dictionary<string, long[]> inputShapes = new Dictionary<string, long[]>();
        public Dictionary<string, Tensor<float>> inputTensors = new Dictionary<string, Tensor<float>>();
        public Dictionary<string, TextureTransform> inputTransforms = new Dictionary<string, TextureTransform>();

        public Tensor<float>[] _modelInputs;
        public bool _dontDestroyInputs = false;
        
        public IEnumerator _schedule;
        public bool _isScheduled;
        public bool _isDisabled;

        public int _numSteps;
        public int[] _stepLayers;
        public UnityEngine.Rendering.CommandBuffer[] _stepCb;
        public bool _stepCbNeedUpdate = false;
        public int _currentStep;

        public int _layersCount;
        public int _startLayer;

        public long _startTimestamp;
        public long _endTimestamp;
        public float _execTime;

        public int _infCount;
        public bool _isStarted;
        public bool _isReady;
        public float _timeLastRun;

        public long _fullExecStartTime;
        public long _fullExecEndTime;
        public float _fullExecTimeMs;
        //public int _fullExecFrames;

        public ModelStats _stats;


        public RuntimeModel(string name, Model model, Worker worker, int numSteps)
        {
            _name = name;
            _model = model;
            _worker = worker;

            // inputs
            inputNames = new string[_model.inputs.Count];
            for (int i = 0; i < _model.inputs.Count; i++)
            {
                inputNames[i] = _model.inputs[i].name;

                int shapeRank = _model.inputs[i].shape.rank;
                long[] shape = new long[shapeRank];

                for(int s = 0; s < shapeRank; s++)
                {
                    shape[s] = Mathf.Max(_model.inputs[i].shape.Get(s), 1);
                }
                inputShapes[inputNames[i]] = shape;

                bool isNHWC = shape[^1] < 10;
                int H = !isNHWC ? (int)shape[^2] : (int)shape[^3];
                int W = !isNHWC ? (int)shape[^1] : (int)shape[^2];
                int C = !isNHWC ? (int)shape[^3] : (int)shape[^1];

                inputTransforms[inputNames[i]] = new TextureTransform().SetDimensions(W, H, C).SetTensorLayout(!isNHWC ? TensorLayout.NCHW : TensorLayout.NHWC);
                inputTensors[inputNames[i]] = new Tensor<float>(new TensorShape(1, (int)shape[^3], (int)shape[^2], (int)shape[^1]));
            }

            // outputs
            outputNames = new string[_model.outputs.Count];
            for (int i = 0; i < _model.outputs.Count; i++)
            {
                outputNames[i] = _model.outputs[i].name;
            }

            _schedule = null;
            _isScheduled = false;

            //_maxLayers = model.layers.Count;
            //_minLayers = (_maxLayers + 7) >> 3;
            //_stepLayers = _maxLayers >> 3;

            _numSteps = numSteps;
            _stepLayers = new int[_numSteps];

            _layersCount = model.layers.Count;
            int numLayers = 0;

            for(int i = 0; i < _numSteps; i++)
            {
                if (i != _numSteps - 1)
                    _stepLayers[i] = _layersCount / _numSteps;
                else
                    _stepLayers[i] = (_layersCount - numLayers);

                numLayers += _stepLayers[i];
            }

            if(kinect.KinectInterop.IsSupportsComputeShaders())
            {
                _stepCbNeedUpdate = true;
            }

            _startLayer = 0;
            _currentStep = 0;

            _startTimestamp = 0;
            _endTimestamp = 0;
            _execTime = 0f;

            _infCount = 0;
            _isStarted = false;
            _isReady = false;
            _timeLastRun = 0f;

            _fullExecStartTime = 0L;
            _fullExecEndTime = 0L;
            _fullExecTimeMs = 0f;
            //_fullExecFrames = 0;
        }

        public override string ToString()
        {
            return $"Model: {_name}, steps: {_numSteps}, layers: {_layersCount}, inf: {_infCount}\nstarted: {_isStarted}, ready: {_isReady}, fs: {_fsIndex}, dTs: {_endTimestamp - _startTimestamp}";
        }
    }


    // queued model inference
    public class ModelInference
    {
        public int _fsIndex;
        public string _modelName;
        public int _stepIndex;

        public string[] _inputNames;
        public Tensor<float>[] _inputTensors;
        public bool _keepInput;


        public ModelInference(int fsIndex, string modelName, int stepIndex = 0)
        {
            this._fsIndex = fsIndex;
            this._modelName = modelName;
            this._stepIndex = stepIndex;
        }

        public void SetInput(string inputName, Tensor<float> inputTensor, bool keepInput = false)
        {
            _inputNames = new string[] { inputName };
            _inputTensors = new Tensor<float>[] { inputTensor };
            _keepInput = keepInput;
        }

        public void SetInput(string[] inputNames, Tensor<float>[] inputTensors, bool keepInput = false)
        {
            _inputNames = inputNames;
            _inputTensors = inputTensors;
            _keepInput = keepInput;
        }

        public override string ToString()
        {
            return $"fs: {_fsIndex}, Model: {_modelName}, step: {_stepIndex}";
        }
    }

    /// <summary>
    /// SentisManager schedules and manages Sentis model inferences.
    /// </summary>
    public class SentisManager : MonoBehaviour
    {

        //[Range(0, 5)]
        //[Tooltip("Number of (free) frames before model runs.")]
        //public uint framesBetweenModelRuns = 0;

        [Tooltip("Whether to try to optimize model inference time, or not.")]
        private bool optimizeInference = false;


        // SentisManager singleton instance
        private static SentisManager _instance = null;
        private bool _isStarted = false;

        // backend type
        //private Unity.Sentis.DeviceType _sentisDeviceType = Unity.Sentis.DeviceType.GPU;
        private BackendType _backendType = BackendType.GPUCompute;

        // maximum allowed inferences per model
        public const int MAX_MODEL_INFERENCES = 1;

        // list of runtime models
        private List<RuntimeModel> _runtimeModels = new List<RuntimeModel>();
        private Dictionary<string, RuntimeModel> _nameToModel = new Dictionary<string, RuntimeModel>();
        private int _currentModel = 0, _lastModel = -1, _lastStep = -1;

        // model inference queue
        private Queue<ModelInference> _inferenceQueue = new Queue<ModelInference>();

        // whether or not to skip the model inference this frame
        private bool _skipFrameInference = false;

        //// frame properties
        //private float _unityTime = 0f;
        ////private float _startFpsTime = 0f;

        //private ulong _frameIndex = 0;
        //private ulong _lastRunIndex = 0;
        ////private float _fpsSmoothed = 0f;


        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static SentisManager Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <summary>
        /// Skip model inference this frame.
        /// </summary>
        public void SkipModelInfThisFrame()
        {
            if(Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                _skipFrameInference = true;
            }
        }

        /// <summary>
        /// Checks if Sentis manager is started or not.
        /// </summary>
        /// <returns>true if SM is started, false otherwise</returns>
        public bool IsStarted()
        {
            return _isStarted;
        }

        /// <summary>
        /// Starts Sentis manager.
        /// </summary>
        public void StartManager()
        {
            if(!_isStarted)
            {
                //if (!kinect.KinectInterop.IsSupportsComputeShaders())
                //{
                //    throw new System.Exception("Sorry, but your device does not support compute shaders.");
                //}

                _isStarted = true;
                _backendType = kinect.KinectInterop.IsSupportsComputeShaders() ? BackendType.GPUCompute : BackendType.GPUPixel;

                //_runtimeModels.Clear();
                //_nameToModel.Clear();
                _currentModel = 0;
                _lastModel = -1;
                _lastStep = -1;

                //// model inference queue
                //_inferenceQueue.Clear();

                //// frame properties
                //_unityTime = 0f;
                //_frameIndex = 0;
                //_lastRunIndex = 0;

                //Debug.Log("Sentis manager started");
            }
        }

        /// <summary>
        /// Stops Sentis manager.
        /// </summary>
        public void StopManager()
        {
            if(_isStarted)
            {
                // dispose model workers and inputs
                foreach (RuntimeModel rtModel in _runtimeModels)
                {
                    rtModel._worker.Dispose();
                    rtModel._worker = null;

                    // dispose command buffers
                    if (rtModel._stepCb != null)
                    {
                        for (int s = 0; s < rtModel._stepCb.Length; s++)
                        {
                            rtModel._stepCb[s]?.Clear();
                            rtModel._stepCb[s]?.Dispose();
                            rtModel._stepCb[s] = null;
                        }

                        rtModel._stepCb = null;
                    }

                    // dispose input tensors
                    if (rtModel._modelInputs != null)
                    {
                        for (int i = rtModel._modelInputs.Length - 1; i >= 0; i--)
                        {
                            rtModel._modelInputs[i].Dispose();
                        }

                        rtModel._modelInputs = null;
                    }

                    foreach(string inputName in rtModel.inputNames)
                    {
                        Tensor<float> inputTensor = rtModel.inputTensors[inputName];
                        inputTensor.Dispose();
                    }

                    rtModel.inputTensors.Clear();
                    rtModel.inputTransforms.Clear();
                }

                // cleanup lists
                _runtimeModels.Clear();
                _nameToModel.Clear();
                //_currentModel = 0;

                // dispose queued inputs
                while (_inferenceQueue.TryDequeue(out ModelInference modelInf))
                {
                    if (modelInf._inputTensors != null)
                    {
                        foreach(Tensor inputTensor in modelInf._inputTensors)
                            inputTensor.Dispose();
                        modelInf._inputTensors = null;
                    }
                }

                // cleanup queue
                _inferenceQueue.Clear();

                _isStarted = false;
                //Debug.Log("Sentis manager stopped");
            }
        }

        /// <summary>
        /// Stops and then restarts Sentis manager.
        /// </summary>
        public void RestartManager()
        {
            StopManager();
            StartManager();
        }


        ///// <summary>
        ///// Loads and returns Sentis model with the specified name, or null if not found.
        ///// </summary>
        ///// <param name="modelName">Model name</param>
        ///// <returns>Sentis model</returns>
        //public Model LoadModel(string modelName)
        //{
        //    string modelPath = System.IO.Path.Combine(Application.persistentDataPath, modelName);
        //    if (!kinect.KinectInterop.CopyResourceFile(modelName, modelPath))
        //    {
        //        Debug.LogError($"Can't find model '{modelName}'");
        //        return null;
        //    }

        //    Model sentisModel = ModelLoader.Load(modelPath);
        //    //ModelAsset modelAsset = Resources.Load<ModelAsset>(modelName);
        //    //Model sentisModel = ModelLoader.Load(modelAsset);

        //    return sentisModel;
        //}

        /// <summary>
        /// Adds model to the execution list.
        /// </summary>
        /// <param name="name">Model's unique ID.</param>
        /// <param name="modelName">Model file name</param>
        /// <returns>Runtime model</returns>
        public RuntimeModel AddModel(string name, string modelName, int steps = 1)
        {
            if (_nameToModel.ContainsKey(name))
                //throw new System.Exception($"Model {name} already exists.");
                return _nameToModel[name];

            modelName += ".sen";
            string modelPath = System.IO.Path.Combine(Application.persistentDataPath, modelName);

            if (!kinect.KinectInterop.CopyResourceFile(modelName, modelPath))
            {
                Debug.LogError($"Can't find model '{modelName}'");
                return null;
            }

            Model model = ModelLoader.Load(modelPath);
            var worker = new Worker(model, _backendType);
            RuntimeModel rtModel = new RuntimeModel(name, model, worker, steps);

            _runtimeModels.Add(rtModel);
            _nameToModel[name] = rtModel;
            //Debug.Log($"Added model: {name}");

            return rtModel;
        }

        ///// <summary>
        ///// Removes model from the list
        ///// </summary>
        ///// <param name="name">Model's name</param>
        ///// <returns>true if removal was successful, false otherwise</returns>
        //public bool RemoveModel(string name)
        //{
        //    if(_nameToModel.ContainsKey(name))
        //    {
        //        RuntimeModel rtModel = _nameToModel[name];

        //        // dispose worker
        //        rtModel._worker.Dispose();
        //        rtModel._worker = null;

        //        // dispose input tensors
        //        if(rtModel._modelInputs != null)
        //        {
        //            for (int i = rtModel._modelInputs.Length - 1; i >= 0; i--)
        //            {
        //                rtModel._modelInputs[i].Dispose();
        //            }

        //            rtModel._modelInputs = null;
        //        }

        //        foreach (string inputName in rtModel.inputNames)
        //        {
        //            Tensor inputTensor = rtModel.inputTensors[inputName];
        //            inputTensor.Dispose();
        //        }

        //        rtModel.inputTensors.Clear();
        //        rtModel.inputTransforms.Clear();

        //        // remove the model
        //        _nameToModel.Remove(name);
        //        _runtimeModels.Remove(rtModel);
        //        //Debug.Log($"Removed model: {name}");

        //        return true;
        //    }

        //    return false;
        //}

        /// <summary>
        /// Disables a runtime model. The model stays in the list, but will not be executed any more.
        /// </summary>
        /// <param name="name">Model name</param>
        /// <param name="isDisable">Whether to disable it or not</param>
        /// <returns>true if the model is successfuly disabled, false otherwise</returns>
        public bool DisableModel(string name, bool isDisable = true)
        {
            if (_nameToModel.ContainsKey(name))
            {
                RuntimeModel rtModel = _nameToModel[name];
                rtModel._isDisabled = isDisable;
                //Debug.Log($"Disabled model: {name}");

                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns runtime model by name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public RuntimeModel GetModel(string name)
        {
            if (_nameToModel.ContainsKey(name))
            {
                return _nameToModel[name];
            }

            return null;
        }

        /// <summary>
        /// Execute the model with one input tensor.
        /// </summary>
        /// <param name="fsIndex">Frameset index</param>
        /// <param name="modelName">Model name</param>
        /// <param name="inputName">Input name</param>
        /// <param name="inputTex">Input texture</param>
        /// <exception cref="System.Exception">If model inference has been started, but not finished yet</exception>
        public bool StartInference(int fsIndex, string modelName, string inputName, Texture inputTex)
        {
            if (_nameToModel.ContainsKey(modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelName];

                //if (rtModel._isStarted)
                if (rtModel._infCount >= MAX_MODEL_INFERENCES)
                {
                    //if (!keepInput && inputTensor != null)
                    //    inputTensor.Dispose();

                    //Debug.LogError($"fs: {fsIndex} - Can't start model {modelName}\nModel {rtModel._name} is scheduled {rtModel._plannedInferences} times.");
                    return false;
                }

                Tensor<float> inputTensor = (Tensor<float>)rtModel.inputTensors[inputName];
                TextureTransform inputTrans = rtModel.inputTransforms[inputName];
                TextureConverter.ToTensor(inputTex, inputTensor, inputTrans);

                // enqueue inference
                //for (int i = 0; i < rtModel._numSteps; i++)
                {
                    ModelInference modelInf = new ModelInference(fsIndex, modelName);  //, i);
                    //if (i == 0)  // only set the input for the first frame
                        modelInf.SetInput(inputName, inputTensor, keepInput: true);
                    _inferenceQueue.Enqueue(modelInf);
                }

                rtModel._infCount++;  // += rtModel._numSteps;
                rtModel._fullExecStartTime = System.DateTime.UtcNow.Ticks;
                //Debug.LogWarning($"  Model {rtModel._name} scheduled ({rtModel._numSteps} steps), fs: {fsIndex}.\ninf: {rtModel._infCount}, started: {rtModel._isStarted}");

                return true;
            }

            return false;
        }

        ///// <summary>
        ///// Execute the model with more than one input tensor(s).
        ///// </summary>
        ///// <param name="fsIndex">Frameset index</param>
        ///// <param name="modelName">Model name</param>
        ///// <param name="inputName">Input name</param>
        ///// <param name="inputTensor">Input tensor</param>
        ///// <returns>true if the model is started successfully, false - if n</returns>
        ///// <exception cref="System.Exception">If model inference has been started, but not finished yet</exception>
        //public bool StartInference(int fsIndex, string modelName, string[] inputNames, Tensor[] inputTensors, bool keepInput = false)
        //{
        //    if (_nameToModel.ContainsKey(modelName))
        //    {
        //        RuntimeModel rtModel = _nameToModel[modelName];

        //        //if (rtModel._isStarted)
        //        if (rtModel._infCount >= MAX_MODEL_INFERENCES)
        //        {
        //            if (!keepInput && inputTensors != null)
        //            {
        //                foreach(Tensor inputTensor in inputTensors)
        //                    inputTensor.Dispose();
        //            }

        //            //Debug.LogError($"fs: {fsIndex} - Can't start model {modelName}\nModel {rtModel._name} is scheduled {rtModel._plannedInferences} times.");
        //            return false;
        //        }

        //        // enqueue inference
        //        ModelInference modelInf = new ModelInference(fsIndex, modelName);  //, i);
        //        modelInf.SetInput(inputNames, inputTensors, keepInput);
        //        _inferenceQueue.Enqueue(modelInf);

        //        rtModel._infCount++;  // += rtModel._numSteps;
        //        rtModel._fullExecStartTime = System.DateTime.UtcNow.Ticks;
        //        //Debug.LogWarning($"  Model {rtModel._name} scheduled ({rtModel._numSteps} steps), fs: {fsIndex}.\ninf: {rtModel._infCount}, started: {rtModel._isStarted}");

        //        return true;
        //    }

        //    return false;
        //}

        /// <summary>
        /// Returns the current frameset-index of the runtime model.
        /// </summary>
        /// <param name="modelName">Model name</param>
        /// <returns>Frameset index</returns>
        public int GetModelFsIndex(string modelName)
        {
            if (_nameToModel.ContainsKey(modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelName];
                return rtModel._fsIndex;
            }

            return -1;
        }

        /// <summary>
        /// Checks if model inference is started.
        /// </summary>
        /// <param name="fsIndex">Frameset index</param>
        /// <param name="modelName">Model name</param>
        /// <returns>true if inference is started, false otherwise</returns>
        public bool IsModelStarted(int fsIndex, string modelName)
        {
            if (_nameToModel.ContainsKey(modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelName];

                if (rtModel._isStarted && rtModel._fsIndex == fsIndex)
                    return true;
                if (rtModel._infCount >= MAX_MODEL_INFERENCES)
                    return true;
            }

            foreach(ModelInference modelInf in _inferenceQueue)
            {
                if (modelInf._modelName == modelName && modelInf._fsIndex == fsIndex)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if model inference is ready.
        /// </summary>
        /// <param name="fsIndex">Frameset index</param>
        /// <param name="modelName">Model name</param>
        /// <returns>true if inference is ready, false otherwise</returns>
        public bool IsModelReady(int fsIndex, string modelName)
        {
            if (_nameToModel.ContainsKey(modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelName];
                return rtModel._isReady && rtModel._fsIndex == fsIndex;
            }

            return false;
        }

        /// <summary>
        /// Clears model-inference ready flag.
        /// </summary>
        /// <param name="fsIndex">Frameset index</param>
        /// <param name="modelName">Model name</param>
        /// <exception cref="System.Exception">If model inference has not been started or not finished yet</exception>
        public void ClearModelReady(int fsIndex, string modelName)
        {
            if (_nameToModel.ContainsKey(modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelName];
                
                if(rtModel._fsIndex == fsIndex)
                {
                    if (!rtModel._isStarted)
                        throw new System.Exception($"Model {modelName}, fs: {rtModel._fsIndex} is not started yet.");
                    if (!rtModel._isReady)
                        throw new System.Exception($"Model {modelName}, fs: {rtModel._fsIndex} is not ready yet.");

                    rtModel._isStarted = false;
                    rtModel._isReady = false;
                    rtModel._startLayer = 0;

                    //Debug.LogWarning($"Cleared ready-flag for model {rtModel._name}, fs: {rtModel._fsIndex}\ninf: {rtModel._infCount}, started: {rtModel._isStarted}");
                }
            }
        }

        /// <summary>
        /// Peeks model output tensor.
        /// </summary>
        /// <param name="modelName">Model name</param>
        /// <param name="outputName">Output tensor name</param>
        /// <returns>Output tensor, or null if model is not found</returns>
        private Tensor PeekModelOutput(int fsIndex, string modelName, string outputName)
        {
            if (_nameToModel.ContainsKey(modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelName];
                return rtModel._worker.PeekOutput(outputName);
            }

            return null;
        }

        /// <summary>
        /// Returns output tensor's shape.
        /// </summary>
        /// <param name="modelName">Model name</param>
        /// <param name="outputName">Output tensor name</param>
        /// <returns>Tensor shape, or 0-shape if model is not found</returns>
        public long[] GetOutputShape(string modelName, string outputName)
        {
            if (_nameToModel.ContainsKey(modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelName];
                Tensor tensor = rtModel._worker.PeekOutput(outputName);

                if(tensor != null)
                {
                    int shapeRank = tensor.shape.rank;
                    long[] shape = new long[shapeRank];

                    for (int s = 0; s < shapeRank; s++)
                    {
                        shape[s] = Mathf.Max(tensor.shape[s], 1);
                    }

                    return shape;
                }
            }

            return new long[0];
        }

        /// <summary>
        /// Returns output tensor as compute buffer.
        /// </summary>
        /// <param name="modelName">Model name</param>
        /// <param name="outputName">Output tensor name</param>
        /// <returns>Compute buffer of the output tensor, or null</returns>
        public ComputeBuffer OutputAsComputeBuffer(int fsIndex, string modelName, string outputName)
        {
            var tensor = PeekModelOutput(fsIndex, modelName, outputName);
            if (tensor == null)
                return null;

            ComputeTensorData gpuTensor = tensor.dataOnBackend as ComputeTensorData;
            if(gpuTensor == null)
                return null;

            return gpuTensor.buffer;
        }

        /// <summary>
        /// Returns output tensor shape.
        /// </summary>
        /// <param name="modelName">Model name</param>
        /// <param name="outputName">Output tensor name</param>
        /// <returns>Tensor shape</returns>
        public TensorShape PeekOutputShape(int fsIndex, string modelName, string outputName)
        {
            var tensor = PeekModelOutput(fsIndex, modelName, outputName);
            if (tensor == null)
                return TensorShape.Ones(4);

            return tensor.shape;
        }


        ///// <summary>
        ///// Gets the name of the current model.
        ///// </summary>
        ///// <returns></returns>
        //public string GetCurrentModelName()
        //{
        //    RuntimeModel rtModel = _runtimeModels[_currentModel];
        //    return rtModel._name;
        //}

        ///// <summary>
        ///// Increments the current model index. 
        ///// </summary>
        //public void GoToNextModel()
        //{
        //    // go to the next model
        //    _currentModel = (_currentModel + 1) % _runtimeModels.Count;
        //}

        /// <summary>
        /// Updates the frame-index of runtime and inferenced models.
        /// </summary>
        /// <param name="isSyncDepthColor">Whether d/c are synched or not</param>
        /// <param name="isSyncBodyDepth">Whether b/d are synched or not</param>
        /// <param name="frameIndex">Open frame index</param>
        public void UpdateRtModelsFrame(bool isSyncDepthColor, bool isSyncBodyDepth, int frameIndex)
        {
            // go through rt models
            int rtModelsCount = _runtimeModels.Count;
            for (int i = 0; i < rtModelsCount; i++)
            {
                RuntimeModel rtModel = _runtimeModels[i];
                int prevFsIndex = rtModel._fsIndex;

                switch (rtModel._name[0])
                {
                    case 'd':
                        // depth
                        rtModel._fsIndex = isSyncDepthColor ? frameIndex : -1;
                        break;
                    case 'b':
                    case 'o':
                        // body or object
                        rtModel._fsIndex = isSyncBodyDepth ? frameIndex : -1;
                        break;
                }

                if(prevFsIndex != rtModel._fsIndex)
                {
                    //Debug.LogWarning($"Updated rt-model {rtModel._name} to fs: {rtModel._fsIndex} (prevFs: {prevFsIndex})");
                }

                _runtimeModels[i] = rtModel;
            }

            // go through inf models
            int infModelsCount = _inferenceQueue.Count;
            for (int i = 0; i < infModelsCount; i++)
            {
                ModelInference infModel = _inferenceQueue.Dequeue();
                int prevFsIndex = infModel._fsIndex;

                switch (infModel._modelName[0])
                {
                    case 'd':
                        // depth
                        infModel._fsIndex = isSyncDepthColor ? frameIndex : -1;
                        break;
                    case 'b':
                    case 'o':
                        // body or object
                        infModel._fsIndex = isSyncBodyDepth ? frameIndex : -1;
                        break;
                }

                if (prevFsIndex != infModel._fsIndex)
                {
                    //Debug.LogWarning($"Updated inf-model {infModel._modelName} to fs: {infModel._fsIndex} (prevFs: {prevFsIndex})");
                }

                _inferenceQueue.Enqueue(infModel);
            }
        }


        /// <summary>
        /// Checks if there are any started but not yet finished rt-model.
        /// </summary>
        /// <returns>true, if there are unfinished models, false otherwise</returns>
        public bool IsAnyRtModelUnfinished()
        {
            int rtModelsCount = _runtimeModels.Count;
            for (int i = 0; i < rtModelsCount; i++)
            {
                RuntimeModel rtModel = _runtimeModels[i];

                if(IsModelStarted(rtModel._fsIndex, rtModel._name))
                {
                    if(IsModelReady(rtModel._fsIndex, rtModel._name))
                    {
                        //Debug.LogWarning($"Clearing rt-model: {rtModel._name}, fs: {rtModel._fsIndex}");
                        ClearModelReady(rtModel._fsIndex, rtModel._name);
                    }
                    else
                    {
                        //Debug.LogWarning($"Found unfinished rt-model: {rtModel._name}, fs: {rtModel._fsIndex}");
                        return true;
                    }
                }
            }

            return false;
        }


        void Awake()
        {
            // set instance or destroy the extra manager
            if(Instance != null) 
            {
                Destroy(this);
                return;
            }

            _instance = this;

            // start sentis-manager
            StartManager();
        }

        void OnDestroy()
        {
            // stop sentis manager
            StopManager();
        }


        // checks if the given inference model is started or not
        private bool IsInfModelGoodToStart(ModelInference modelInf)
        {
            if (_nameToModel.ContainsKey(modelInf._modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelInf._modelName];
                return rtModel != null && !rtModel._isStarted;
            }

            return false;
        }

        // sets the runtime model inference properties
        private RuntimeModel ModelInf2RuntimeModel(ModelInference modelInf)
        {
            if (_nameToModel.ContainsKey(modelInf._modelName))
            {
                RuntimeModel rtModel = _nameToModel[modelInf._modelName];

                if(rtModel != null)
                {
                    if(rtModel._isStarted && modelInf._stepIndex == 0)
                    {
                        Debug.LogError($"Model {rtModel._name}, fs {rtModel._fsIndex} already started!");
                    }

                    if(modelInf._stepIndex == 0 && !rtModel._isDisabled)
                    {
                        rtModel._fsIndex = modelInf._fsIndex;
                        rtModel._isStarted = true;
                        rtModel._isReady = false;

                        rtModel._startLayer = 0;
                        rtModel._currentStep = 0;
                        //rtModel._fullExecStartTime = System.DateTime.UtcNow.Ticks;

                        if (modelInf._inputTensors != null)
                        {
                            rtModel._modelInputs = modelInf._inputTensors;
                            rtModel._dontDestroyInputs = modelInf._keepInput;
                        }

                        rtModel._isScheduled = true;
                        //Debug.LogWarning($"    Model {rtModel._name} started, fs: {rtModel._fsIndex}.\ninf: {rtModel._infCount}, started: {rtModel._isStarted}, scheduled: {rtModel._isScheduled}");
                    }

                    return rtModel;
                }
            }

            return null;
        }


        /// <summary>
        /// Updates the runtime models
        /// </summary>
        void LateUpdate()
        {
            //if (_unityTime == Time.unscaledTime)
            //    return;

            //// set frame props
            //_unityTime = Time.unscaledTime;
            //_frameIndex++;

            //// leave at least one frame between model runs 
            //if ((_frameIndex - _lastRunIndex) < (framesBetweenModelRuns + 1))
            //{
            //    //Debug.Log($"  skipping model run in frame {_frameIndex}. LastRun: {_lastRunIndex}");
            //    return;
            //}

            if (_lastModel >= 0 && _lastStep >= 0)
            {
                if(optimizeInference)
                {
                    TryOptimizeInference();
                }

                _lastModel = -1;
                _lastStep = -1;
            }

            if(_skipFrameInference)
            {
                // skip model inference this frame
                _skipFrameInference = false;
                Debug.Log($"Skipping model inference this frame, frame: {kinect.UniCamInterface._frameIndex}");
                return;
            }

            // execute and check models
            int rtModelsCount = _runtimeModels.Count;
            for(int i = 0; i < rtModelsCount; i++)
            {
                RuntimeModel rtModel = _runtimeModels[_currentModel];

                _lastModel = _currentModel;
                _currentModel = (_currentModel + 1) % rtModelsCount;

                bool isOkToStart = false;
                if(_inferenceQueue.TryPeek(out ModelInference modelInf))
                {
                    isOkToStart = IsInfModelGoodToStart(modelInf);
                }

                if (isOkToStart)
                {
                    modelInf = _inferenceQueue.Dequeue();
                    ModelInf2RuntimeModel(modelInf);
                }

                if(rtModel._stepCbNeedUpdate && rtModel._dontDestroyInputs)  // only allowed for fixed inputs
                {
                    // update step cb's as needed
                    UpdateStepCb(rtModel);
                    rtModel._stepCbNeedUpdate = false;
                }

                if (rtModel != null && rtModel._isScheduled)
                {
                    _lastStep = rtModel._currentStep;

                    if (ExecModelAndCheckReady(rtModel, Time.unscaledTime))  // _unityTime))
                    {
                        //_lastRunIndex = _frameIndex;
                        break;
                    }
                    else
                    {
                        _lastModel = -1;
                        _lastStep = -1;
                    }
                }

                //_currentModel = (_currentModel + 1) % rtModelsCount;
            }
        }

        // update step cb's of a runtime model
        private void UpdateStepCb(RuntimeModel rtModel)
        {
            if(rtModel._stepCb != null)
            {
                for (int s = 0; s < rtModel._stepCb.Length; s++)
                {
                    rtModel._stepCb[s]?.Clear();
                    rtModel._stepCb[s]?.Dispose();
                    rtModel._stepCb[s] = null;
                }

                rtModel._stepCb = null;
            }

            rtModel._stepCb = new UnityEngine.Rendering.CommandBuffer[rtModel._numSteps];
            IEnumerator modelSchedule = null;
            bool hasMoreWork = false;

            for (int s = 0; s < rtModel._numSteps; s++)
            {
                rtModel._stepCb[s] = new UnityEngine.Rendering.CommandBuffer();
                SetCommandBuffer(rtModel._worker, rtModel._stepCb[s]);

                int layersToRun = rtModel._stepLayers[s];

                if(s == 0)
                {
                    if (rtModel._numSteps > 1)
                    {
                        modelSchedule = rtModel._modelInputs != null ? rtModel._worker.ScheduleIterable(rtModel._modelInputs) : rtModel._worker.ScheduleIterable();
                        hasMoreWork = modelSchedule.MoveNext();
                    }
                    else
                    {
                        if (rtModel._modelInputs != null)
                            rtModel._worker.Schedule(rtModel._modelInputs);
                        else
                            rtModel._worker.Schedule();
                    }
                }

                if (modelSchedule != null)
                {
                    for (int i = 0; i < layersToRun; i++)
                    {
                        hasMoreWork = modelSchedule.MoveNext();
                        if (!hasMoreWork)
                            break;
                    }
                }
            }

        }

        // executes portion of model and checks if the execution is finished.
        private bool ExecModelAndCheckReady(RuntimeModel rtModel, float unityTime)
        {
            // check if it's already executed this frame
            if (rtModel._timeLastRun != unityTime && rtModel._isScheduled)
            {
                // schedule number of layers
                rtModel._timeLastRun = unityTime;
                //bool wasReady = rtModel._isReady;

                if (!rtModel._isReady)
                {
                    rtModel._startTimestamp = System.DateTime.UtcNow.Ticks;

                    //Debug.Log($"rtModel {rtModel._name}, step: {rtModel._currentStep}, start: {rtModel._startLayer}, count: {rtModel._layersCount}");
                    int stepLayers = rtModel._stepLayers[rtModel._currentStep];
                    int numLayers = 0;
                    bool hasMoreWork = false;

                    if(rtModel._stepCb == null)
                    {
                        if (rtModel._numSteps > 1)
                        {
                            if(rtModel._currentStep == 0)
                            {
                                rtModel._schedule = rtModel._modelInputs != null ? rtModel._worker.ScheduleIterable(rtModel._modelInputs) : rtModel._worker.ScheduleIterable();
                                rtModel._schedule.MoveNext();
                            }
                        }
                        else
                        {
                            if (rtModel._modelInputs != null)
                                rtModel._worker.Schedule(rtModel._modelInputs);
                            else
                                rtModel._worker.Schedule();
                        }

                        if (rtModel._schedule != null)
                        {
                            try
                            {
                                do
                                {
                                    hasMoreWork = rtModel._schedule.MoveNext();
                                    if (++numLayers % stepLayers == 0)
                                        break;
                                }
                                while (hasMoreWork);
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogException(ex);
                            }
                        }
                        else
                        {
                            // schedule in one go
                            hasMoreWork = false;
                        }
                    }
                    else
                    {
                        Graphics.ExecuteCommandBuffer(rtModel._stepCb[rtModel._currentStep]);
                        hasMoreWork = rtModel._currentStep < (rtModel._numSteps - 1);
                    }

                    rtModel._endTimestamp = System.DateTime.UtcNow.Ticks;
                    rtModel._execTime = (rtModel._endTimestamp - rtModel._startTimestamp) * 0.0001f;
                    //rtModel._fullExecFrames++;

                    rtModel._currentStep++;
                    rtModel._startLayer += numLayers;
                    rtModel._isReady = !hasMoreWork;

                    //Debug.LogWarning($"    fs: {rtModel._fsIndex}, Model {rtModel._name}, step: {rtModel._currentStep}/{rtModel._numSteps}, layers: {numLayers}\nstartLayer: {rtModel._startLayer - numLayers}, time: {rtModel._execTime:F3} ms, frame: {kinect.UniCamInterface._frameIndex}, ready: {rtModel._isReady}");
                }

                // check if model has finished execution
                if (rtModel._isReady && rtModel._isScheduled)
                {
                    rtModel._schedule = null;
                    rtModel._isScheduled = false;
                    rtModel._infCount--;  // -= rtModel._numSteps;

                    rtModel._fullExecEndTime = System.DateTime.UtcNow.Ticks;
                    rtModel._fullExecTimeMs = (rtModel._fullExecEndTime - rtModel._fullExecStartTime) * 0.0001f;
                    //Debug.LogWarning($"Model {rtModel._name}, fs {rtModel._fsIndex} is ready in {rtModel._currentStep} step(s)\ninf: {rtModel._infCount}, started: {rtModel._isStarted}, layers: {rtModel._layersCount}, time: {rtModel._fullExecTimeMs:F3} ms, frame: {kinect.UniCamInterface._frameIndex}");

                    if (!rtModel._dontDestroyInputs && rtModel._modelInputs != null)
                    {
                        // dispose input tensors
                        for (int i = rtModel._modelInputs.Length - 1; i >= 0; i--)
                        {
                            rtModel._modelInputs[i].Dispose();
                        }
                    }

                    rtModel._modelInputs = null;
                }

                return true;
            }

            return false;
        }


        // try to optimize the inference times
        private void TryOptimizeInference()
        {
            RuntimeModel rtModel = _lastModel >= 0 && _lastModel < _runtimeModels.Count ? _runtimeModels[_lastModel] : null;
            if (rtModel == null)
                return;

            if (rtModel._stats == null)
            {
                SentisStats.InitModelStats(rtModel, isSetWaitTime: true, isOptSteps: true);
            }

            if (!rtModel._stats._optimizeSteps)
                return;

            // set model step time
            SentisStats.SetModelStepTime(rtModel, _lastStep, Time.unscaledDeltaTime);

            if (_lastStep == (rtModel._numSteps - 1))
            {
                if (rtModel._stats._timeCount >= SentisStats.STAT_COUNT)
                {
                    // try to optimize layers per step
                    float maxInfTime = 1.2f / (Application.targetFrameRate > 0 ? Application.targetFrameRate : 30);  // tolerate 20% more
                    bool optSteps = SentisStats.TryOptimizeModelSteps(rtModel, maxInfTime);

                    // restart model stats
                    SentisStats.InitModelStats(rtModel, isSetWaitTime: false, isOptSteps: optSteps);
                }
            }
        }


        private static MethodInfo _setCommandBufferMethod;
        private static object[] _setCommandBufferArgs = new object[1];

        // sets worker's command buffer
        public static void SetCommandBuffer(Worker worker, UnityEngine.Rendering.CommandBuffer cb)
        {
            if (_setCommandBufferMethod == null)
            {
                _setCommandBufferMethod = typeof(Worker).GetMethod("SetCommandBuffer",
                    BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            _setCommandBufferArgs[0] = cb;
            _setCommandBufferMethod.Invoke(worker, _setCommandBufferArgs);
        }


        // save tensor data to a file
        public static void SaveTensorData(string fileName, Tensor<float> tensor, int countPerRow)
        {
            StringBuilder sbBuf = new StringBuilder();

            Tensor<float> localTensor = tensor.ReadbackAndClone();
            float[] tensorData = tensor.DownloadToArray();
            localTensor.Dispose();

            int h = tensorData.Length / countPerRow;
            int w = countPerRow;

            for (int y = 0, i = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++, i++)
                {
                    sbBuf.AppendFormat("{0:F3}", tensorData[i]).Append(';');
                }

                if (sbBuf.Length > 0 && sbBuf[sbBuf.Length - 1] == ';')
                    sbBuf.Remove(sbBuf.Length - 1, 1);

                sbBuf.AppendLine();
            }

            //Debug.Log(fileName + "\n" + sbBuf.ToString());
            System.IO.File.WriteAllText(fileName, sbBuf.ToString());
        }

    }
}
