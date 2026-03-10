using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.rfilkov.trackers
{
    public class KalmanFilter
    {
        protected readonly float _stdWeightPosition;
        protected readonly float _stdWeightVelocity;

        protected Matrix4x4 _posMotionMat, _sizeMotionMat;
        protected Matrix4x4 _updateMat;


        /// <summary>
        /// 
        /// </summary>
        /// <param name="stdWeightPosition"></param>
        /// <param name="stdWeightVelocity"></param>
        public KalmanFilter(float stdWeightPosition = 1f / 20f, float stdWeightVelocity = 1f / 160f)
        {
            _stdWeightPosition = stdWeightPosition;
            _stdWeightVelocity = stdWeightVelocity;

            _posMotionMat = Matrix4x4.identity;
            _posMotionMat[0, 2] = 1f;
            _posMotionMat[1, 3] = 1f;

            _sizeMotionMat = Matrix4x4.identity;
            _sizeMotionMat[0, 2] = 1f;
            _sizeMotionMat[1, 3] = 1f;

            _updateMat = Matrix4x4.identity;
        }

        /// <summary>
        /// Create track from unassociated measurement
        /// </summary>
        /// <param name="f1x8Mean">the mean matrix(1x8 dimensional) of the new track.</param>
        /// <param name="f8x8Covariance">the covariance matrix(8x8 dimensional) of the new track.</param>
        /// <param name="measurement">Bounding box coordinates(x, y, a, h) with center position(x, y), aspect ratio a, and height h.</param>
        public virtual void Initiate(ref Vector4 posMean, ref Vector4 sizeMean, ref Matrix4x4 posCov, ref Matrix4x4 sizeCov, Vector4 measurement)
        {
            posMean = new Vector4(measurement.x, measurement.y, 0f, 0f);
            sizeMean = new Vector4(measurement.z, measurement.w, 0f, 0f);
            //Debug.Log($"  init-posMean: {posMean}\nsizeMean: {sizeMean}");

            float h = measurement.w;
            float p = (2f * _stdWeightPosition * h) * (2f * _stdWeightPosition * h);
            float v = (10f * _stdWeightVelocity * h) * (10f * _stdWeightVelocity * h);

            posCov = Matrix4x4.zero;
            posCov.m00 = p;
            posCov.m11 = p;
            posCov.m22 = v;
            posCov.m33 = v;

            sizeCov = Matrix4x4.zero;
            sizeCov.m00 = 1e-2f;
            sizeCov.m11 = p;
            sizeCov.m22 = 1e-5f;
            sizeCov.m33 = v;
            //Debug.Log($"  init-posCov:\n{posCov}\nsizeCov:\n{sizeCov}");
        }

        /// <summary>
        /// Run Kalman filter prediction step.
        /// </summary>
        /// <param name="f1x8Mean">the mean matrix(1x8 dimensional) of the new track.</param>
        /// <param name="f8x8Covariance">the covariance matrix(8x8 dimensional) of the new track.</param>
        public virtual void Predict(ref Vector4 posMean, ref Vector4 sizeMean, ref Matrix4x4 posCov, ref Matrix4x4 sizeCov)
        {
            var h = sizeMean.y;
            float p = (_stdWeightPosition * h) * (_stdWeightPosition * h);
            float v = (_stdWeightVelocity * h) * (_stdWeightVelocity * h);

            Matrix4x4 posMotionCov = Matrix4x4.zero;
            posMotionCov.m00 = p;
            posMotionCov.m11 = p;
            posMotionCov.m22 = v;
            posMotionCov.m33 = v;

            Matrix4x4 sizeMotionCov = Matrix4x4.zero;
            sizeMotionCov.m00 = 1e-2f;
            sizeMotionCov.m11 = p;
            sizeMotionCov.m22 = 1e-5f;
            sizeMotionCov.m33 = v;

            posMean = _posMotionMat * posMean;
            sizeMean = _sizeMotionMat * sizeMean;
            //Debug.Log($"  pred-posMean: {posMean}\nsizeMean: {sizeMean}");

            posCov = MatOps.Add(_posMotionMat * posCov * Matrix4x4.Transpose(_posMotionMat), posMotionCov);
            sizeCov = MatOps.Add(_sizeMotionMat * sizeCov * Matrix4x4.Transpose(_sizeMotionMat), sizeMotionCov);
            //Debug.Log($"  pred-posCov:\n{posCov}\nsizeCov:\n{sizeCov}");
        }

        /// <summary>
        /// Run Kalman filter correction step.
        /// </summary>
        /// <param name="f1x8Mean">The predicted state's mean matrix (1x8 dimensional).</param>
        /// <param name="f8x8Covariance">The state's covariance matrix (8x8 dimensional).</param>
        /// <param name="measurement">The 4 dimensional measurement vector(x, y, a, h), where(x, y) is the center position, a the aspect ratio, and h the height of the bounding box.</param>
        public virtual void Update(ref Vector4 posMean, ref Vector4 sizeMean, ref Matrix4x4 posCov, ref Matrix4x4 sizeCov, Vector4 measurement)
        {
            Project(ref posMean, ref sizeMean, ref posCov, ref sizeCov, out Vector4 projMean, out Matrix4x4 posProjCov, out Matrix4x4 sizeProjCov);

            Matrix4x4 posChoFactor = MatOps.ChoFactor(posProjCov, 2);
            Matrix4x4 posUpdateCov = posCov * Matrix4x4.Transpose(_updateMat);
            Matrix4x4 posKalmanGain = Matrix4x4.Transpose(MatOps.ChoSolve(posChoFactor, posUpdateCov, 2));
            //Debug.Log($"  upd-posChoFactor:\n{posChoFactor}\nposKalmanGain:\n{posKalmanGain}");

            Matrix4x4 sizeChoFactor = MatOps.ChoFactor(sizeProjCov, 2);
            Matrix4x4 sizeUpdateCov = sizeCov * Matrix4x4.Transpose(_updateMat);
            Matrix4x4 sizeKalmanGain = Matrix4x4.Transpose(MatOps.ChoSolve(sizeChoFactor, sizeUpdateCov, 2));
            //Debug.Log($"  upd-sizeChoFactor:\n{sizeChoFactor}\nsizeKalmanGain:\n{sizeKalmanGain}");

            Vector4 moveInnov = measurement - projMean;
            Vector4 posInnov = new Vector4(moveInnov.x, moveInnov.y, 0f, 0f);
            Vector4 sizeInnov = new Vector4(moveInnov.z, moveInnov.w, 0f, 0f);
            //Debug.Log($"  upd-moveInnov: {moveInnov}\nposInnov: {posInnov}, sizeInnov: {sizeInnov}");

            //float[,] tmp1 = MatOps.Mul(moveInnov, Matrix4x4.Transpose(posKalmanGain));
            //posMean = MatOps.Add(posMean, tmp1);
            posMean += Matrix4x4.Transpose(posKalmanGain) * posInnov;
            sizeMean += Matrix4x4.Transpose(sizeKalmanGain) * sizeInnov;
            //Debug.Log($"  upd-posMean: {posMean}\nsizeMean: {sizeMean}");

            //f8x8Covariance -= f8x4KalmanGain * f4x4ProjectedCov * f8x4KalmanGain.Transpose();
            //float[,] tmp2 = MatOps.Mul(MatOps.Mul(posKalmanGain, projCov), MatOps.Transpose(posKalmanGain));
            //posCov = MatOps.Sub(posCov, tmp2);
            posCov = MatOps.Sub(posCov, posKalmanGain * posProjCov * Matrix4x4.Transpose(posKalmanGain));
            sizeCov = MatOps.Sub(sizeCov, sizeKalmanGain * sizeProjCov * Matrix4x4.Transpose(sizeKalmanGain));
            //Debug.Log($"  upd-posCov:\n{posCov}\nsizeCov:\n{sizeCov}");
        }

        /// <summary>
        /// Project state distribution to measurement space.
        /// </summary>
        /// <param name="f1x8Mean">The state's mean matrix (1x8 dimensional).</param>
        /// <param name="f8x8Covariance">The state's covariance matrix (8x8 dimensional).</param>
        /// <param name="f1x4ProjectedMean">Returns the projected mean matrix of the given state estimate.</param>
        /// <param name="f4x4ProjectedCov">Returns the projected covariance matrix of the given state estimate.</param>
        protected virtual void Project(ref Vector4 posMean, ref Vector4 sizeMean, ref Matrix4x4 posCov, ref Matrix4x4 sizeCov,
            out Vector4 projMean, out Matrix4x4 posProjCov, out Matrix4x4 sizeProjCov)
        {
            var h = sizeMean.y;
            float p = (_stdWeightPosition * h) * (_stdWeightPosition * h);

            Matrix4x4 posInnovCov = Matrix4x4.zero;
            posInnovCov.m00 = p;
            posInnovCov.m11 = p;

            Vector4 posSizeMean = new Vector4(posMean.x, posMean.y, sizeMean.x, sizeMean.y);
            projMean = _updateMat * posSizeMean;
            //Debug.Log($"  proj-posSizeMean: {posSizeMean}\nprojMean: {projMean}");

            Matrix4x4 posOnlyCov = new Matrix4x4(new Vector4(posCov.m00, posCov.m10, 0f, 0f), new Vector4(posCov.m01, posCov.m11, 0f, 0f), Vector4.zero, Vector4.zero);
            posProjCov = MatOps.Add(_updateMat * posOnlyCov * Matrix4x4.Transpose(_updateMat), posInnovCov);
            //Debug.Log($"  proj-posOnlyCov:\n{posOnlyCov}\nposProjCov:\n{posProjCov}");

            Matrix4x4 sizeInnovCov = Matrix4x4.zero;
            sizeInnovCov.m00 = 1e-1f;
            sizeInnovCov.m11 = p;

            Matrix4x4 sizeOnlyCov = new Matrix4x4(new Vector4(sizeCov.m00, sizeCov.m10, 0f, 0f), new Vector4(sizeCov.m01, sizeCov.m11, 0f, 0f), Vector4.zero, Vector4.zero);
            sizeProjCov = MatOps.Add(_updateMat * sizeOnlyCov * Matrix4x4.Transpose(_updateMat), sizeInnovCov);
            //Debug.Log($"  proj-sizeOnlyCov:\n{sizeOnlyCov}\nsizeProjCov:\n{sizeProjCov}");
        }
    }

}
