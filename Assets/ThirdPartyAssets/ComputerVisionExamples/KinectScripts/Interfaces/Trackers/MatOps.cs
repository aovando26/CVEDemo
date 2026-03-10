using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.rfilkov.trackers
{
    public static class MatOps
    {

        /// <summary>
        /// Checks if two matrices are equal.
        /// </summary>
        /// <param name="mat1">Matrix1</param>
        /// <param name="mat2">Matrix2</param>
        /// <returns>true if matrices are equal, false otherwise</returns>
        public static bool IsEqual(float[,] mat1, float[,] mat2)
        {
            int rowCount1 = mat1.GetLength(0);
            int colCount1 = mat1.GetLength(1);

            int rowCount2 = mat2.GetLength(0);
            int colCount2 = mat2.GetLength(1);

            if (rowCount1 != rowCount2 || colCount1 != colCount2)
                return false;

            for (int i = 0; i < rowCount1; i++)
            {
                for (int j = 0; j < colCount1; j++)
                {
                    if (mat1[i, j] != mat2[i, j])
                        return false;
                }
            }

            return true;
        }


        /// <summary>
        /// Returns the string representation of a matrix.
        /// </summary>
        /// <param name="mat">Matrix</param>
        /// <returns>String representation</returns>
        public static string ToString(float[,] mat)
        {
            if (mat == null)
                return "(null)";

            System.Text.StringBuilder sbBuf = new System.Text.StringBuilder();

            int rowCount = mat.GetLength(0);
            int colCount = mat.GetLength(1);

            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    sbBuf.Append($"{mat[i, j]:F2}").Append('\t');
                }

                sbBuf.AppendLine();
            }

            return sbBuf.ToString();
        }


        /// <summary>
        /// Returns the string representation of a matrix.
        /// </summary>
        /// <param name="vec">Vector</param>
        /// <returns>String representation</returns>
        public static string ToString(float[] vec)
        {
            if (vec == null)
                return "(null)";

            System.Text.StringBuilder sbBuf = new System.Text.StringBuilder();

            int elemCount = vec.Length;
            for (int i = 0; i < elemCount; i++)
            {
                sbBuf.Append($"{vec[i]:F2}").Append('\t');
            }

            return sbBuf.ToString();
        }


        /// <summary>
        /// Returns the string representation of a matrix.
        /// </summary>
        /// <param name="vec">Vector</param>
        /// <returns>String representation</returns>
        public static string ToString(int[] vec)
        {
            if (vec == null)
                return "(null)";

            System.Text.StringBuilder sbBuf = new System.Text.StringBuilder();

            int elemCount = vec.Length;
            for (int i = 0; i < elemCount; i++)
            {
                sbBuf.Append($"{vec[i]}").Append('\t');
            }

            return sbBuf.ToString();
        }


        /// <summary>
        /// Returns the transpose of the matrix.
        /// </summary>
        /// <param name="mat">Matrix</param>
        /// <returns>Transpose of the matrix</returns>
        public static float[,] Transpose(float[,] mat)
        {
            int rowCount = mat.GetLength(0);
            int colCount = mat.GetLength(1);

            float[,] result = new float[colCount, rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    result[j, i] = mat[i, j];
                }
            }

            return result;
        }

        /// <summary>
        /// Adds two matrices and returns the result.
        /// </summary>
        /// <param name="mat1">Matrix1</param>
        /// <param name="mat2">Matrix2</param>
        /// <returns>Result</returns>
        public static float[,] Add(float[,] mat1, float[,] mat2)
        {
            int rowCount1 = mat1.GetLength(0);
            int colCount1 = mat1.GetLength(1);

            int rowCount2 = mat2.GetLength(0);
            int colCount2 = mat2.GetLength(1);

            if (rowCount1 != rowCount2 || colCount1 != colCount2)
                throw new System.Exception($"mat1[{rowCount1},{colCount1}] must have the same dimensions as mat2[{rowCount2},{colCount2}]");

            float[,] result = new float[rowCount1, colCount2];

            for (int i = 0; i < rowCount1; i++)
            {
                for (int j = 0; j < colCount1; j++)
                {
                    result[i, j] = mat1[i, j] + mat2[i, j];
                }
            }

            return result;
        }

        /// <summary>
        /// Adds two 4x4 matrices and returns the result.
        /// </summary>
        /// <param name="lhs">Matrix1</param>
        /// <param name="rhs">Matrix2</param>
        /// <returns>Result</returns>
        public static Matrix4x4 Add(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            Matrix4x4 res = default(Matrix4x4);

            res.m00 = lhs.m00 + rhs.m00;
            res.m01 = lhs.m01 + rhs.m01;
            res.m02 = lhs.m02 + rhs.m02;
            res.m03 = lhs.m03 + rhs.m03;

            res.m10 = lhs.m10 + rhs.m10;
            res.m11 = lhs.m11 + rhs.m11;
            res.m12 = lhs.m12 + rhs.m12;
            res.m13 = lhs.m13 + rhs.m13;

            res.m20 = lhs.m20 + rhs.m20;
            res.m21 = lhs.m21 + rhs.m21;
            res.m22 = lhs.m22 + rhs.m22;
            res.m23 = lhs.m23 + rhs.m23;

            res.m30 = lhs.m30 + rhs.m30;
            res.m31 = lhs.m31 + rhs.m31;
            res.m32 = lhs.m32 + rhs.m32;
            res.m33 = lhs.m33 + rhs.m33;

            return res;
        }

        /// <summary>
        /// Subtracts two matrices and returns the result.
        /// </summary>
        /// <param name="mat1">Matrix1</param>
        /// <param name="mat2">Matrix2</param>
        /// <returns>Result</returns>
        public static float[,] Sub(float[,] mat1, float[,] mat2)
        {
            int rowCount1 = mat1.GetLength(0);
            int colCount1 = mat1.GetLength(1);

            int rowCount2 = mat2.GetLength(0);
            int colCount2 = mat2.GetLength(1);

            if (rowCount1 != rowCount2 || colCount1 != colCount2)
                throw new System.Exception($"mat1[{rowCount1},{colCount1}] must have the same dimensions as mat2[{rowCount2},{colCount2}]");

            float[,] result = new float[rowCount1, colCount2];

            for (int i = 0; i < rowCount1; i++)
            {
                for (int j = 0; j < colCount1; j++)
                {
                    result[i, j] = mat1[i, j] - mat2[i, j];
                }
            }

            return result;
        }

        /// <summary>
        /// Subtracts 4x4 matrix2 from 4x4 matrix1, and returns the result.
        /// </summary>
        /// <param name="lhs">Matrix1</param>
        /// <param name="rhs">Matrix2</param>
        /// <returns>Result</returns>
        public static Matrix4x4 Sub(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            Matrix4x4 res = default(Matrix4x4);

            res.m00 = lhs.m00 - rhs.m00;
            res.m01 = lhs.m01 - rhs.m01;
            res.m02 = lhs.m02 - rhs.m02;
            res.m03 = lhs.m03 - rhs.m03;

            res.m10 = lhs.m10 - rhs.m10;
            res.m11 = lhs.m11 - rhs.m11;
            res.m12 = lhs.m12 - rhs.m12;
            res.m13 = lhs.m13 - rhs.m13;

            res.m20 = lhs.m20 - rhs.m20;
            res.m21 = lhs.m21 - rhs.m21;
            res.m22 = lhs.m22 - rhs.m22;
            res.m23 = lhs.m23 - rhs.m23;

            res.m30 = lhs.m30 - rhs.m30;
            res.m31 = lhs.m31 - rhs.m31;
            res.m32 = lhs.m32 - rhs.m32;
            res.m33 = lhs.m33 - rhs.m33;

            return res;
        }

        /// <summary>
        /// Multiplies two matrices and returns the result.
        /// </summary>
        /// <param name="mat1">Matrix1</param>
        /// <param name="mat2">Matrix2</param>
        /// <returns>Result</returns>
        public static float[,] Mul(float[,] mat1, float[,] mat2)
        {
            int rowCount1 = mat1.GetLength(0);
            int colCount1 = mat1.GetLength(1);

            int rowCount2 = mat2.GetLength(0);
            int colCount2 = mat2.GetLength(1);

            if (colCount1 != rowCount2)
                throw new System.Exception($"mat1.colCount({colCount1}) must be equal to mat2.rowCount({rowCount2})");

            float[,] result = new float[rowCount1, colCount2];

            for (int i = 0; i < rowCount1; i++)
            {
                for (int j = 0; j < colCount2; j++)
                {
                    float s = 0f;
                    for (int k = 0; k < colCount1; k++)
                    {
                        s += mat1[i, k] * mat2[k, j];
                    }

                    result[i, j] = s;
                }
            }

            return result;
        }


        /// <summary>
        /// Decompose a matrix into lower triangular.
        /// </summary>
        /// <param name="mat">Matrix</param>
        /// <returns>Result</returns>
        public static float[,] ChoFactor(float[,] mat)
        {
            int rowCount = mat.GetLength(0);
            int colCount = mat.GetLength(1);

            if (rowCount != colCount)
                throw new System.Exception($"Row-count({rowCount}) must be equal to col-count({colCount})");

            float[,] lower = new float[rowCount, rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    float sum = 0f;

                    if (j == i)
                    {
                        // summation for diagonals
                        for (int k = 0; k < j; k++)
                            sum += (lower[j, k] * lower[j, k]);

                        lower[j, j] = Mathf.Sqrt(mat[j, j] - sum);
                    }
                    else
                    {
                        for (int k = 0; k < j; k++)
                            sum += (lower[i, k] * lower[j, k]);

                        lower[i, j] = (mat[i, j] - sum) / lower[j, j];
                    }
                }
            }

            return lower;
        }

        /// <summary>
        /// Decompose a square matrix (A) into lower triangular matrix (L) such that L * L' = A.
        /// </summary>
        /// <param name="mat">Matrix (A)</param>
        /// <returns>Result</returns>
        public static Matrix4x4 ChoFactor(Matrix4x4 mat, int rcCount = 4)
        {
            Matrix4x4 lower = Matrix4x4.zero;

            for (int i = 0; i < rcCount; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    float sum = 0f;

                    if (j == i)
                    {
                        // summation for diagonals
                        for (int k = 0; k < j; k++)
                            sum += (lower[j, k] * lower[j, k]);

                        lower[j, j] = Mathf.Sqrt(mat[j, j] - sum);
                    }
                    else
                    {
                        for (int k = 0; k < j; k++)
                            sum += (lower[i, k] * lower[j, k]);

                        lower[i, j] = (mat[i, j] - sum) / lower[j, j];
                    }
                }
            }

            //Debug.Log($"ChoFactor Lower:\n{lower}\nL*L:\n{lower * Matrix4x4.Transpose(lower)}\nMat:\n{mat}");

            return lower;
        }

        /// <summary>
        /// Uses Cholesky decomposed lower triangular matrix to solve (L * L' * X = B) for X, and returns the result.
        /// </summary>
        /// <param name="lower">Lower triangular matrix</param>
        /// <param name="mat">Matrix (B)</param>
        /// <returns>Result</returns>
        public static float[,] ChoSolve(float[,] lower, float[,] mat)
        {
            int rowCountL = lower.GetLength(0);
            int colCountL = lower.GetLength(1);

            int rowCount = mat.GetLength(0);
            int colCount = mat.GetLength(1);

            if (rowCountL != colCountL)
                throw new System.Exception($"Cho-factor must be square, but row-count({rowCountL}) is not equal to col-count({colCountL})");
            if (rowCountL != rowCount)
                throw new System.Exception($"Cho-factor row-count({rowCountL}) must be equal to matrix row-count({rowCount})");

            // forward substitution (L*Y=B)
            float[,] resultY = new float[rowCount, colCount];

            for (int i = 0; i < rowCount; i++)
            {
                for (int j = 0; j < colCount; j++)
                {
                    float s = 0f;
                    for (int k = 0; k < i; k++)
                    {
                        s += lower[i, k] * resultY[k, j];
                    }

                    resultY[i, j] = (mat[i, j] - s) / lower[i, i];
                }
            }

            //Debug.Log($"Y:\n{ToString(resultY)}");

            // backward substitution (L'*X=Y)
            float[,] resultX = new float[rowCount, colCount];

            for (int i = rowCount - 1; i >= 0; i--)
            {
                for (int j = colCount - 1; j >= 0; j--)
                {
                    float s = 0f;
                    for (int k = rowCount - 1; k > i; k--)
                    {
                        s += lower[k, i] * resultX[k, j];
                    }

                    resultX[i, j] = (resultY[i, j] - s) / lower[i, i];
                }
            }

            //Debug.Log($"X:\n{ToString(resultX)}");

            return resultX;
        }

        /// <summary>
        /// Uses Cholesky decomposed lower triangular matrix to solve (L*L'*X=B) for X, and returns the result.
        /// </summary>
        /// <param name="lower">Lower triangular matrix</param>
        /// <param name="mat">Matrix</param>
        /// <returns>Result</returns>
        public static Matrix4x4 ChoSolve(Matrix4x4 lower, Matrix4x4 mat, int rcCount = 4)
        {
            // forward substitution (L*Y=B)
            Matrix4x4 resultY = Matrix4x4.zero;

            for (int i = 0; i < rcCount; i++)
            {
                for (int j = 0; j < rcCount; j++)
                {
                    float s = 0f;
                    for (int k = 0; k < i; k++)
                    {
                        s += lower[i, k] * resultY[k, j];
                    }

                    resultY[i, j] = (mat[i, j] - s) / lower[i, i];
                }
            }

            // backward substitution (L'*X=Y)
            Matrix4x4 resultX = Matrix4x4.zero;

            for (int i = rcCount - 1; i >= 0; i--)
            {
                for (int j = rcCount - 1; j >= 0; j--)
                {
                    float s = 0f;
                    for (int k = rcCount - 1; k > i; k--)
                    {
                        s += lower[k, i] * resultX[k, j];
                    }

                    resultX[i, j] = (resultY[i, j] - s) / lower[i, i];
                }
            }

            //Debug.Log($"ChoSolve Y:\n{resultY}\nX:\n{resultX}\nL * L' * X:\n{lower * Matrix4x4.Transpose(lower) * resultX}\nMat:\n{mat}");

            return resultX;
        }


    }
}
