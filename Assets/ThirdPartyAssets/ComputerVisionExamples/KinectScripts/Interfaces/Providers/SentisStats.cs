using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace com.rfilkov.providers
{
    // model step statistics data
    public class ModelStats
    {
        public float[,] _stepTimes;
        public float[] _avgTimes;
        public int _timeIndex;
        public int _timeCount;

        public float _waitTime;
        public bool _optimizeSteps;
        public RuntimeModel _rtModel;


        public override string ToString()
        {
            System.Text.StringBuilder sbBuf = new();
            sbBuf.AppendLine($"{_rtModel._name} - {_rtModel._numSteps} steps, idx: {_timeIndex}, cnt: {_timeCount}, opt: {_optimizeSteps}");

            int stepCount = _avgTimes != null ? _avgTimes.Length : _rtModel._numSteps;
            int avgCount = _timeCount;

            for (int s = 0; s < stepCount; s++)
            {
                sbBuf.Append($"{s}-");
                avgCount = 0;

                if (_stepTimes != null)
                {
                    int timeCount = _stepTimes.GetLength(1);

                    for (int t = 0; t < timeCount; t++)
                    {
                        float stepTime = _stepTimes[s, t];
                        sbBuf.Append($"{stepTime:F3} ");

                        if (stepTime > 0f)
                            avgCount++;
                    }
                }

                if (_avgTimes != null)
                {
                    //sbBuf.AppendLine($"avg: {_avgTimes[s] / Mathf.Max(avgCount, 1):F3}/{avgCount}");
                    sbBuf.AppendLine($"avg: {_avgTimes[s]:F3}/{avgCount}");
                }
            }

            return sbBuf.ToString();
        }
    }

    // sentis-statistics methods
    public static class SentisStats
    {
        // number of stats per model step
        public const int STAT_COUNT = 15;
        // initial wait time, in seconds (model calls)
        public const float STAT_WAIT_TIME = 1f;
        // maximum number of model steps
        public const int MAX_STEPS_COUNT = 10;
        // minimum number of layers in a step
        public const int MIN_STEP_LAYERS = 20;


        // inits the model stats structure
        public static void InitModelStats(RuntimeModel rtModel, bool isSetWaitTime = false, bool isOptSteps = false)
        {
            rtModel._stats = new ModelStats()
            {
                _stepTimes = new float[rtModel._numSteps, STAT_COUNT],
                _avgTimes = new float[rtModel._numSteps],
                _timeIndex = 0,
                _timeCount = 0,

                _waitTime = isSetWaitTime ? STAT_WAIT_TIME : 0f,
                _optimizeSteps = isOptSteps,
                _rtModel = rtModel
            };
        }


        // sets the model step time of execution
        public static void SetModelStepTime(RuntimeModel rtModel, int stepIndex, float deltaTime)
        {
            if(rtModel._stats._waitTime > 0f)
            {
                rtModel._stats._waitTime -= deltaTime;
                if (rtModel._stats._waitTime <= 0f && stepIndex != (rtModel._numSteps - 1))
                    rtModel._stats._waitTime += deltaTime;

                return;
            }

            if (rtModel._stats._stepTimes != null)
                rtModel._stats._stepTimes[stepIndex, rtModel._stats._timeIndex] = deltaTime;
            if (rtModel._stats._avgTimes != null)
                rtModel._stats._avgTimes[stepIndex] = rtModel._stats._avgTimes[stepIndex] > 0f ? Mathf.Min(deltaTime, rtModel._stats._avgTimes[stepIndex]) : deltaTime;  // += deltaTime;

            if(stepIndex == (rtModel._numSteps - 1))
            {
                rtModel._stats._timeIndex = (rtModel._stats._timeIndex + 1) % STAT_COUNT;
                rtModel._stats._timeCount++;
            }

            //Debug.Log($"{rtModel._name} - step {stepIndex}, frame: {kinect.UniCamInterface._frameIndex}, dtime: {deltaTime:F3}/{kinect.UniCamInterface._frameTs:F3}\n{rtModel._stats}");
        }

        // tries to rearrange model step to optimize inference time
        public static bool TryOptimizeModelSteps(RuntimeModel rtModel, float maxInfTime)
        {
            // find average times
            int stepCount = rtModel._numSteps;
            int stepIndex = -1;

            //Debug.Log($"before: {rtModel._stats}");
            for (int s = 0; s < stepCount; s++)
            {
                //int timeCount = rtModel._stats._timeCount;
                //float avgStepTime = rtModel._stats._avgTimes[s] / timeCount;

                //float varStepTime = 0f;
                //for(int t = 0; t < STAT_COUNT; t++)
                //{
                //    float stepTime = rtModel._stats._stepTimes[s, t];
                //    varStepTime += (stepTime - avgStepTime) * (stepTime - avgStepTime);
                //}

                //varStepTime /= STAT_COUNT;
                //float sdStepTime = Mathf.Sqrt(varStepTime);

                //for (int t = 0; t < STAT_COUNT; t++)
                //{
                //    float stepTime = rtModel._stats._stepTimes[s, t];

                //    if(stepTime < (avgStepTime - sdStepTime) || stepTime > (avgStepTime + sdStepTime))
                //    {
                //        rtModel._stats._stepTimes[s, t] = 0f;
                //        rtModel._stats._avgTimes[s] -= stepTime;
                //        timeCount--;
                //    }
                //}

                float avgStepTime = rtModel._stats._avgTimes[s];  // / Mathf.Max(timeCount, 1);
                if (avgStepTime > maxInfTime && rtModel._stepLayers[s] > MIN_STEP_LAYERS && stepCount < MAX_STEPS_COUNT)
                {
                    stepIndex = s;
                    break;
                }
            }

            if(stepIndex >= 0)
            {
                // move part of the layers to the next step
                int newStepCount = stepIndex != (stepCount - 1) ? stepCount : stepCount + 1;
                int[] newStepLayers = new int[newStepCount];

                for (int s = 0; s < stepCount; s++)
                    newStepLayers[s] = rtModel._stepLayers[s];

                int moveLayers = rtModel._stepLayers[stepIndex] / 3;  // 33%
                newStepLayers[stepIndex] -= moveLayers;
                newStepLayers[stepIndex + 1] += moveLayers;

                rtModel._stepLayers = newStepLayers;
                rtModel._numSteps = newStepCount;

                if (kinect.KinectInterop.IsSupportsComputeShaders())
                {
                    rtModel._stepCbNeedUpdate = true;
                }

                Debug.Log($"{rtModel._name} - opt: {stepIndex >= 0}, maxTime: {maxInfTime:F3}, step: {stepIndex}, lCnt: {rtModel._numSteps}/{stepCount}\nlayers: " + string.Join(", ", rtModel._stepLayers) + $"\n{rtModel._stats}");
            }

            return (stepIndex >= 0);
        }

    }

}
