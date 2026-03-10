using System;
using System.Collections;
using System.Collections.Generic;


namespace com.rfilkov.trackers
{
    public static class LAPJV
    {
        /// <summary>
        /// Executes LAPJV algorithm.
        /// </summary>
        /// <param name="cost"></param>
        /// <param name="extendCost"></param>
        /// <param name="costLimit"></param>
        /// <returns></returns>
        public static void Solve(string lapName, float[,] cost, float costLimit, out int[] rowSol, out int[] colSol)
        {
            int rows = cost.GetLength(0);
            int cols = cost.GetLength(1);
            int n = rows;

            if (costLimit != 0f)
            {
                n = (rows >= cols ? rows : cols) + 1;

                //if(n != rows || n != cols)
                {
                    cost = ExtendCostMatrix(lapName, n, cost, costLimit);
                }
            }

            LapjvInternal(n, cost, out rowSol, out colSol);

            if (n != rows || n != cols)
            {
                for (int i = 0; i < n; i++)
                {
                    if (i >= rows || rowSol[i] >= cols)
                        rowSol[i] = -1;

                    if (i >= cols || colSol[i] >= rows)
                        colSol[i] = -1;
                }
            }

            //UnityEngine.Debug.Log($"{lapName} - r: {rows}, c: {cols}, n: {n}, limit: {costLimit}\n{MatOps.ToString(cost)}\nrowSol: {MatOps.ToString(rowSol)}\ncolSol: {MatOps.ToString(colSol)}\n");

            //Array.Resize(ref rowSol, rows);
            //Array.Resize(ref colSol, cols);
        }


        // extends the cost matrix with default values to rectangular form
        private static float[,] ExtendCostMatrix(string lapName, int n, float[,] cost, float costLimit)
        {
            int nRows = cost.GetLength(0);
            int nCols = cost.GetLength(1);

            //int n = rows + cols;  // nRows >= nCols ? nRows : nCols;
            float[,] extendedCost = new float[n, n];
            float defaultValue = costLimit != 0f ? costLimit /** * 0.5f */ : MaxValue(cost) + 1f;

            for (int i = 0; i < nRows; i++)
                for (int j = 0; j < nCols; j++)
                    extendedCost[i, j] = cost[i, j];

            if(n > nRows)
            {
                for (int i = nRows; i < n; i++)
                    for (int j = 0; j < n; j++)
                        extendedCost[i, j] = defaultValue;
            }
            
            if(n > nCols)
            {
                for (int i = 0; i < n; i++)
                    for (int j = nCols; j < n; j++)
                        extendedCost[i, j] = defaultValue;
            }

            // by default the array is initialized with 0s
            //for (int i = nRows; i < n; i++)
            //    for (int j = nCols; j < n; j++)
            //        extendedCost[i, j] = 0f;

            return extendedCost;
        }

        // executes LAP-JV solver.
        private static int LapjvInternal(int n, float[,] cost, out int[] rowSol, out int[] colSol)
        {
            int ret;
            int[] freeRows = new int[n];
            float[] v = new float[n];

            rowSol = new int[n];
            colSol = new int[n];

            ret = ColumnReduction(n, cost, freeRows, rowSol, colSol, v);

            int i = 0;
            while (ret > 0 && i < 2)
            {
                ret = AugmentRowReduction(n, cost, ret, freeRows, rowSol, colSol, v);
                i++;
            }

            if (ret > 0)
            {
                ret = AugmentSolution(n, cost, ret, freeRows, rowSol, colSol, v);
            }

            return ret;
        }

        // finds the max cell value in the matrix
        private static float MaxValue(float[,] matrix)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            float maxVal = float.MinValue;

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (matrix[i, j] > maxVal)
                        maxVal = matrix[i, j];
                }
            }

            return maxVal;
        }

        // Column-reduction and reduction transfer for a dense cost matrix (CcrrtDense).
        private static int ColumnReduction(int n, float[,] cost, int[] freeRows, int[] rowSol, int[] colSol, float[] v)
        {
            int numFree = 0;
            int[] matches = new int[n];
            //bool[] unique = new bool[n];
            //Array.Fill(unique, true);

            for (int j = 0; j < n; j++)
            {
                rowSol[j] = -1;

                v[j] = cost[0, j];
                colSol[j] = 0;
            }

            for (int j = n - 1; j >= 0; j--)
            {
                float min = cost[0, j];
                int imin = 0;

                for (int i = 1; i < n; i++)
                {
                    float c = cost[i, j];

                    if (c < min)
                    {
                        min = c;
                        imin = i;
                    }
                }

                v[j] = min;
                //colSol[j] = imin;

                if (++matches[imin] == 1)
                {
                    // init assignment if minimum row assigned for first time.
                    rowSol[imin] = j;
                    colSol[j] = imin;
                }
                else if (v[j] < v[rowSol[imin]])
                {
                    int j1 = rowSol[imin];
                    rowSol[imin] = j;

                    colSol[j] = imin;
                    colSol[j1] = -1;
                }
                else
                {
                    colSol[j] = -1;        // row already assigned, column not assigned.
                }
            }

            //for (int j = n - 1; j >= 0; j--)
            //{
            //    int i = colSol[j];

            //    if (rowSol[i] < 0)
            //    {
            //        rowSol[i] = j;
            //    }
            //    else
            //    {
            //        unique[i] = false;
            //        colSol[j] = -1;
            //    }
            //}

            for (int i = 0; i < n; i++)
            {
                //if (rowSol[i] < 0)
                if (matches[i] == 0)     // fill list of unassigned 'free' rows.
                {
                    freeRows[numFree++] = i;
                }
                //else if (unique[i])
                if (matches[i] == 1)   // transfer reduction from rows that are assigned once.
                {
                    int j1 = rowSol[i];
                    float min = float.MaxValue;

                    for (int j = 0; j < n; j++)
                    {
                        if (j == j1)
                            continue;

                        float c = cost[i, j] - v[j];
                        if (c < min)
                        {
                            min = c;
                        }
                    }

                    v[j1] -= min;
                }
            }

            return numFree;
        }

        // Augmenting row reduction for a dense cost matrix (CarrDense).
        private static int AugmentRowReduction(int n, float[,] cost, int prevNumFree, int[] freeRows, int[] rowSol, int[] colSol, float[] v)
        {
            int current = 0;
            int numFree = 0;
            //int rrCnt = 0;

            // scan all free rows.
            // in some cases, a free row may be replaced with another one to be scanned next.
            while (current < prevNumFree)
            {
                int i = freeRows[current++];
                //rrCnt++;

                // find minimum and second minimum reduced cost over columns
                int j1 = 0;
                float umin = cost[i, 0] - v[0];

                int j2 = -1;
                float usubmin = float.MaxValue;

                for (int j = 1; j < n; j++)
                {
                    float h = cost[i, j] - v[j];

                    if (h < usubmin)
                    {
                        if (h >= umin)
                        {
                            usubmin = h;
                            j2 = j;
                        }
                        else
                        {
                            usubmin = umin;
                            umin = h;
                            j2 = j1;
                            j1 = j;
                        }
                    }
                }

                int i0 = colSol[j1];
                //float v1New = v[j1] - (usubmin - umin);
                //bool v1Lowers = v1New < v[j1];

                //if (rrCnt < current * n)
                //{
                //    if (v1Lowers)
                //    {
                //        v[j1] = v1New;
                //    }
                //    else if (i0 >= 0 && j2 >= 0)
                //    {
                //        j1 = j2;
                //        i0 = colSol[j2];
                //    }

                //    if (i0 >= 0)
                //    {
                //        if (v1Lowers)
                //        {
                //            freeRows[--current] = i0;
                //        }
                //        else
                //        {
                //            freeRows[numFree++] = i0;
                //        }
                //    }
                //}
                //else
                //{
                //    if (i0 >= 0)
                //    {
                //        freeRows[numFree++] = i0;
                //    }
                //}

                if (umin < usubmin)
                {
                    // change the reduction of the minimum column to increase the minimum
                    // reduced cost in the row to the subminimum.
                    v[j1] = v[j1] - (usubmin - umin);
                }
                else
                {
                    // minimum and subminimum are equal
                    if (i0 >= 0 && j2 >= 0)  // minimum column j1 is assigned.
                    {
                        // swap columns j1 and j2, as j2 may be unassigned.
                        j1 = j2;
                        i0 = colSol[j2];
                    }
                }

                // (re-)assign i to j1, possibly de-assigning an i0.
                rowSol[i] = j1;
                colSol[j1] = i;

                if (i0 >= 0)
                {
                    // minimum column j1 assigned earlier.
                    if (umin < usubmin)
                    {
                        // put in current index, and go back to that index.
                        // continue augmenting path i - j1 with i0.
                        freeRows[--current] = i0;
                    }
                    else
                    {
                        // no further augmenting reduction possible.
                        // store i0 in list of free rows for next phase.
                        freeRows[numFree++] = i0;
                    }
                }
            }

            return numFree;
        }

        // Augment for a dense cost matrix (CaDense).
        private static int AugmentSolution(int n, float[,] cost, int nFreeRows, int[] freeRows, int[] rowSol, int[] colSol, float[] v)
        {
            int[] pred = new int[n];

            for (int f = 0; f < nFreeRows; f++)
            {
                int freeRow = freeRows[f];
                int i = -1;
                int k = 0;

                int j = FindPathDense(n, cost, freeRow, colSol, v, pred);
                if (j < 0 || j >= n)
                    throw new InvalidOperationException($"j:{j} out of scope.");

                while (i != freeRow)
                {
                    i = pred[j];
                    colSol[j] = i;

                    // swap indices j <-> x[i]
                    (rowSol[i], j) = (j, rowSol[i]);
                    k++;

                    if (k >= n)
                        throw new InvalidOperationException($"k:{k} >= n:{n}.");
                }
            }

            return 0;
        }

        //private static void SwapIndices(ref int a, ref int b) => (b, a) = (a, b);

        // Single iteration of modified Dijkstra shortest path algorithm as explained in the JV paper.
        // Returns the closest free column index.
        private static int FindPathDense(int n, float[,] cost, int freeRow, int[] colSol, float[] v, int[] pred)
        {
            int lo = 0, hi = 0;
            int finalJ = -1;
            int nReady = 0;

            int[] collist = new int[n];
            float[] d = new float[n];

            for (int j = 0; j < n; j++)
            {
                collist[j] = j;
                pred[j] = freeRow;
                d[j] = cost[freeRow, j] - v[j];
            }

            while (finalJ == -1)
            {
                // no more columns to be scanned for current minimum.
                if (lo == hi)
                {
                    nReady = lo;
                    hi = FindDense(n, lo, d, collist, colSol);

                    for (int k = lo; k < hi; k++)
                    {
                        int j = collist[k];

                        if (colSol[j] < 0)
                        {
                            finalJ = j;
                            break;
                        }
                    }
                }

                if (finalJ == -1)
                {
                    finalJ = ScanDense(n, cost, ref lo, ref hi, d, collist, pred, colSol, v);
                }
            }

            float mind = d[collist[lo]];
            for (int k = 0; k < nReady; k++)
            {
                int j = collist[k];
                v[j] += d[j] - mind;
            }

            return finalJ;
        }

        // Find columns with minimum d[j] and put them on the SCAN list.
        private static int FindDense(int n, int lo, float[] d, int[] collist, int[] colSol)
        {
            int hi = lo + 1;
            float mind = d[collist[lo]];

            for (int k = hi; k < n; k++)
            {
                int j = collist[k];

                if (d[j] <= mind)
                {
                    if (d[j] < mind)
                    {
                        hi = lo;
                        mind = d[j];
                    }

                    collist[k] = collist[hi];
                    collist[hi++] = j;
                }
            }

            return hi;
        }

        // Scan all columns in TODO starting from arbitrary column in SCAN
        // and try to decrease d of the TODO columns using the SCAN column.
        private static int ScanDense(int n, float[,] cost, ref int lo, ref int hi, float[] d, int[] collist, int[] pred, int[] colSol, float[] v)
        {
            float h, cred_ij;

            while (lo != hi)
            {
                int j = collist[lo++];
                int i = colSol[j];

                float mind = d[j];
                h = cost[i, j] - v[j] - mind;

                // For all columns in TODO
                for (int k = hi; k < n; k++)
                {
                    j = collist[k];
                    cred_ij = cost[i, j] - v[j] - h;

                    if (cred_ij < d[j])
                    {
                        d[j] = cred_ij;
                        pred[j] = i;

                        if (cred_ij == mind)
                        {
                            if (colSol[j] < 0)
                                return j;

                            collist[k] = collist[hi];
                            collist[hi++] = j;
                        }
                    }
                }
            }

            return -1;
        }

    }
}