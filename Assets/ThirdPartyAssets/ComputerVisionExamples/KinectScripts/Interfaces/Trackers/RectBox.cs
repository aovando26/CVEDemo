using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.rfilkov.trackers
{
    public class RectBox
    {
        public float xCenter;
        public float yCenter;
        public float width;
        public float height;

        public float aspect;

        public float xMin;
        public float yMin;
        public float xMax;
        public float yMax;


        public RectBox(float xCenter, float yCenter, float width, float height)
        {
            UpdateRect(xCenter, yCenter, width, height);
        }


        public void UpdateRect(float xCenter, float yCenter, float width, float height)
        {
            this.xCenter = xCenter;
            this.yCenter = yCenter;

            this.width = width;
            this.height = height;
            this.aspect = width / height;

            this.xMin = xCenter - width * 0.5f;
            this.yMin = yCenter - height * 0.5f;
            this.xMax = xCenter + width * 0.5f;
            this.yMax = yCenter + height * 0.5f;
        }


        //public float X { get => x; set => x = value; }

        //public float Y { get => y; set => y = value; }

        //public float Width { get => width; set => width = value; }

        //public float Height { get => height; set => height = value; }


        public override string ToString()
        {
            return $"({xCenter:F2}; {yCenter:F2}; {width:F2}; {height:F2})";
        }


        public Vector4 ToXYAH() => new Vector4(xCenter, yCenter, aspect, height);


        public float CalcIoU(RectBox other)
        {
            float iw = Mathf.Min(xMax, other.xMax) - Mathf.Max(xMin, other.xMin);
            float iou = 0f;

            if (iw > 0)
            {
                float ih = Mathf.Min(yMax, other.yMax) - Mathf.Max(yMin, other.yMin);

                if (ih > 0)
                {
                    float isec = iw * ih;

                    float union = width * height + other.width * other.height - isec;
                    iou = isec / union;
                }
            }

            return iou;
        }


        public float CalcCenterDist(RectBox other)
        {
            return Mathf.Abs(xCenter - other.xCenter) + Mathf.Abs(yCenter - other.yCenter);
        }

    }
}