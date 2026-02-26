using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.rfilkov.trackers
{
    public class TestTracker : MonoBehaviour
    {

        float[,] opMat1 = { { 1, 2, 3 },
                            { 4, 5, 6 } };

        float[,] opMat2 = { { 7, 8 },
                            { 9, 10 },
                            { 11, 12 } };

        float[,] opMatMul = { { 58, 64 },
                              { 139, 154 } };

        float[,] choMatA1 = { { 4, 12, -16 },
                              { 12, 37, -43 },
                              { -16, -43, 98 } };

        float[,] choMatA2 = { { 4, 10, 8 },
                              { 10, 26, 26 },
                              { 8, 26, 61 } };

        float[,] choMatB2 = { { 44 },
                              { 128 },
                              { 214 } };

        float[,] choMatA3 = { { 4, 10, 8 },
                              { 10, 26, 26 },
                              { 8, 26, 61 } };

        float[,] choMatB3 = { { 5, 6, 7, 8 },
                              { 9, 10, 11, 12 },
                              { 13, 14, 15, 16 } };

        void Start()
        {
            //Debug.Log($"OpMat1:\n{MatOps.ToString(opMat1)}");
            //Debug.Log($"OpMat2:\n{MatOps.ToString(opMat2)}");

            //float[,] mat1T = MatOps.Transpose(opMat1);
            //Debug.Log($"Mat1T:\n{MatOps.ToString(mat1T)}");
            //float[,] mat2T = MatOps.Transpose(opMat2);
            //Debug.Log($"Mat2T:\n{MatOps.ToString(mat2T)}");

            //float[,] mat1A = MatOps.Add(opMat1, opMat1);
            //Debug.Log($"Mat1A:\n{MatOps.ToString(mat1A)}");
            //float[,] mat2S = MatOps.Sub(opMat2, opMat2);
            //Debug.Log($"Mat2S:\n{MatOps.ToString(mat2S)}");

            //float[,] mat12M = MatOps.Mul(opMat1, opMat2);
            //Debug.Log($"Mat12M:\n{MatOps.ToString(mat12M)}");
            //Debug.Log($"Mat12M-check:\n{MatOps.IsEqual(mat12M, opMatMul)}");

            //float[,] matChoL1 = MatOps.ChoFactor(choMatA1);
            //Debug.Log($"ChoA1:\n{MatOps.ToString(choMatA1)}\nChoFactor1:\n{MatOps.ToString(matChoL1)}\nChoFactor1T:\n{MatOps.ToString(MatOps.Transpose(matChoL1))}");

            //float[,] matChoL2 = MatOps.ChoFactor(choMatA2);
            //Debug.Log($"ChoA2:\n{MatOps.ToString(choMatA2)}\nChoFactor2:\n{MatOps.ToString(matChoL2)}\nChoFactor2T:\n{MatOps.ToString(MatOps.Transpose(matChoL2))}");
            //float[,] matChoX2 = MatOps.ChoSolve(matChoL2, choMatB2);
            //Debug.Log($"ChoX2:\n{MatOps.ToString(matChoX2)}");

            float[,] matChoL3 = MatOps.ChoFactor(choMatA3);
            Debug.Log($"ChoA3:\n{MatOps.ToString(choMatA3)}\nChoFactor2:\n{MatOps.ToString(matChoL3)}\nChoFactor2T:\n{MatOps.ToString(MatOps.Transpose(matChoL3))}");
            float[,] matChoX3 = MatOps.ChoSolve(matChoL3, choMatB3);
            Debug.Log($"ChoX3:\n{MatOps.ToString(matChoX3)}");
            float[,] matChoB3 = MatOps.Mul(choMatA3, matChoX3);
            Debug.Log($"MatChoB3:\n{MatOps.ToString(matChoB3)}\nchoB3:\n{MatOps.ToString(choMatB3)}");
        }


        void Update()
        {

        }

    }
}
