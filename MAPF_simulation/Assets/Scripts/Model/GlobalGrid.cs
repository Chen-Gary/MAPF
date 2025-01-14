﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using MAPF.Utils;
using MAPF.UI;


namespace MAPF {
    /// <summary>
    /// Cloud in the cloud-edge-terminal architecture
    /// 全局控制中心
    /// </summary>
    public class GlobalGrid {

        #region Const
        private static readonly Dictionary<int, MapUnitEntity.MapUnitType> int2MapUnitType =
                new Dictionary<int, MapUnitEntity.MapUnitType>() {
                    { 0, MapUnitEntity.MapUnitType.PUBLIC_ROAD },
                    { 1, MapUnitEntity.MapUnitType.BARRIER }
                };
        #endregion

        public static GlobalGrid _instance;     //singleton

        public MapUnitEntity[,] gridMap;
        public RobotEntity[,] gridRobot;
        public float[,] globalHeatmap;

        public int dimX = 0;
        public int dimY = 0;

        private Queue<Task> GlobalTaskQueue;
        private int finishedTaskCount = 0;
        private int totalDistanceCovered = 0;
        private SimulationConfig config;

        public GlobalGrid() {
            if (_instance != null) {
                Debug.LogError("[GlobalGrid] singleton constructed more than once");
                return;
            }
            _instance = this;

            this.dimX = 0;
            this.dimY = 0;
            gridMap = null;
            gridRobot = null;

            GlobalTaskQueue = new Queue<Task>();
            config = SimulationEntry.instance._config;
        }

        #region Robot
        public bool UpdateRobotPos(RobotEntity robot, Coord nextPos) {
            Coord currentPos = robot.position;
            // if `currentPos` is consistent with `gridRobot`
            if (robot != gridRobot[currentPos.x, currentPos.y]) {   //they should be the same instance
                Debug.LogError("[GlobalGrid] currentPos of robot is inconsistent with `gridRobot`");
                return false;
            }
            // if nextPos is valid
            if (gridRobot[nextPos.x, nextPos.y].type != RobotEntity.RobotType.NONE && 
                gridRobot[nextPos.x, nextPos.y] != robot /*robot is allowed to stay at current position*/) {
                Debug.LogWarning(string.Format("[GlobalGrid] the position intended to move {0} to is already occupied", nextPos.ToString()));
                return false;
            }
            if (!gridMap[nextPos.x, nextPos.y].canEnter) {
                Debug.LogError("[GlobalGrid] the position intended to move to cannot be entered");
                return false;
            }

            // update `gridRobot`
            robot.position = nextPos;
            gridRobot[nextPos.x, nextPos.y].position = currentPos;

            gridRobot[currentPos.x, currentPos.y] = gridRobot[nextPos.x, nextPos.y];    //set current position to NONE
            gridRobot[nextPos.x, nextPos.y] = robot;


            // update global heatmap
            switch(config._globalHeatmapAlgorithm) {
                case SimulationConfig.GlobalHM.NoHeatmap:
                    _UpdateHeatmapByLoopingRobots(_UpdateHeatmap_NoHeatmap);
                    break;
                case SimulationConfig.GlobalHM.Naive:
                    _UpdateHeatmapByLoopingRobots(_UpdateHeatmap_Naive);
                    break;
                case SimulationConfig.GlobalHM.TShape:
                    _UpdateHeatmapByLoopingRobots(_UpdateHeatmap_TShape);
                    break;
                case SimulationConfig.GlobalHM.CircleGaussian:
                    _UpdateHeatmapByLoopingRobots(_UpdateHeatmap_CircleGaussian);
                    break;
                default:
                    Debug.LogError("[GlobalGrid] invalid _globalHeatmapAlgorithm");
                    break;
            }

            // update UI
            totalDistanceCovered += Coord.ManhattanDistance(currentPos, nextPos);
            UIInfoManager.instance.RenderTotalDistance(totalDistanceCovered);

            return true;
        }

        public void RemoveRobot(RobotEntity robot) {
            Coord pos = robot.position;
            if (robot != gridRobot[pos.x, pos.y]) {   //they should be the same instance
                Debug.LogError("[GlobalGrid] currentPos of robot is inconsistent with `gridRobot`");
                return;
            }
            gridRobot[pos.x, pos.y] = new RobotEntity(RobotEntity.RobotType.NONE, new Coord(pos.x, pos.y));
        }
        #endregion

        #region Heatmap
        private void _UpdateHeatmapByLoopingRobots(Action<FreightRobot> func) {
            this.globalHeatmap = new float[dimX, dimY];  //automatically init to all 0

            for (int x = 0; x < dimX; x++) {
                for (int y = 0; y < dimY; y++) {
                    if (gridRobot[x, y].type == RobotEntity.RobotType.FREIGHT) {
                        //error check
                        if (gridRobot[x, y].position != new Coord(x, y)) {
                            Debug.LogError(string.Format("[GlobalGrid] inconsistency in robot pos {0} and its index found {1}",
                                gridRobot[x, y].position.ToString(), new Coord(x, y).ToString()));
                            return;
                        }
                        func((FreightRobot)gridRobot[x, y]);
                    }
                }
            }
        }
        private void _UpdateHeatmap_NoHeatmap(FreightRobot robot) {
            return;
        }

        private void _UpdateHeatmap_Naive(FreightRobot robot) {
            Coord baseCoord = robot.position;
            globalHeatmap[baseCoord.x, baseCoord.y] += config._Naive_weight;
        }

        private int T_SHAPE_LEN = 2;
        private float T_SHAPE_SCALAR_1 = 2f;
        private float T_SHAPE_SCALAR_2 = 1f;
        private void _UpdateHeatmap_TShape(FreightRobot robot) {
            Coord.UnitDirection prevMove = robot.prevMove;

            // if no move in last step, use Naive algorithm
            if (prevMove == Coord.UnitDirection.ZERO) {
                _UpdateHeatmap_Naive(robot);
                return;
            }

            // T-Shape
            Coord.UnitDirection[] directions = new Coord.UnitDirection[] {
                Coord.UnitDirection.LEFT, Coord.UnitDirection.RIGHT, 
                Coord.UnitDirection.UP, Coord.UnitDirection.DOWN
            };
            foreach (Coord.UnitDirection direction in directions) {
                Coord deltaCoord = Coord.UnitDirection2DeltaCoord(direction);
                if (prevMove == direction) {
                    for (int i = 0; i <= T_SHAPE_LEN; i++) {
                        _IncrementHeatmapIfPossible(this.globalHeatmap, robot.position + i* deltaCoord, T_SHAPE_SCALAR_1);
                    }
                } else if (Coord.IsOnAxisAndPerpendicular(prevMove, direction)) {
                    _IncrementHeatmapIfPossible(this.globalHeatmap, robot.position + deltaCoord, T_SHAPE_SCALAR_2);
                }
            }
        }
        public void TShapeExcludeSelf(FreightRobot robot, float[,] localHeatmap) {
            Coord.UnitDirection prevMove = robot.prevMove;

            if (prevMove == Coord.UnitDirection.ZERO) {
                return;
            }

            // T-Shape
            Coord.UnitDirection[] directions = new Coord.UnitDirection[] {
                Coord.UnitDirection.LEFT, Coord.UnitDirection.RIGHT,
                Coord.UnitDirection.UP, Coord.UnitDirection.DOWN
            };
            foreach (Coord.UnitDirection direction in directions) {
                Coord deltaCoord = Coord.UnitDirection2DeltaCoord(direction);
                if (prevMove == direction) {
                    for (int i = 0; i <= T_SHAPE_LEN; i++) {
                        _IncrementHeatmapIfPossible(localHeatmap, robot.position + i * deltaCoord, -T_SHAPE_SCALAR_1);
                    }
                } else if (Coord.IsOnAxisAndPerpendicular(prevMove, direction)) {
                    _IncrementHeatmapIfPossible(localHeatmap, robot.position + deltaCoord, -T_SHAPE_SCALAR_2);
                }
            }
        }


        private int CIRCLE_RADIUS = 4;
        private double GAUSSIAN_SCALAR = 5;     //The app will crash if this value is >= 6. I do not know why.
        public void _UpdateHeatmap_CircleGaussian(FreightRobot robot) {
            int startX = robot.position.x - CIRCLE_RADIUS;
            int startY = robot.position.y - CIRCLE_RADIUS;

            int circle_diameter = 2 * CIRCLE_RADIUS;
            for (int i = 0; i <= circle_diameter; i++) {
                for (int j = 0; j <= circle_diameter; j++) {
                    Coord currentCoord = new Coord(startX + i, startY + j);
                    double euclideanDistance = Coord.EuclideanDistance(currentCoord, robot.position);
                    if (euclideanDistance <= CIRCLE_RADIUS) {
                        double heat = GAUSSIAN_SCALAR * Coord.GaussianDistribution(euclideanDistance, 0, 1);
                        _IncrementHeatmapIfPossible(this.globalHeatmap, currentCoord, (float)heat);
                    }   
                }
            }
        }
        public void CircleGaussianExcludeSelf(FreightRobot robot, float[,] localHeatmap) {
            int startX = robot.position.x - CIRCLE_RADIUS;
            int startY = robot.position.y - CIRCLE_RADIUS;

            int circle_diameter = 2 * CIRCLE_RADIUS;
            for (int i = 0; i <= circle_diameter; i++) {
                for (int j = 0; j <= circle_diameter; j++) {
                    Coord currentCoord = new Coord(startX + i, startY + j);
                    double euclideanDistance = Coord.EuclideanDistance(currentCoord, robot.position);
                    if (euclideanDistance <= CIRCLE_RADIUS) {
                        double heat = GAUSSIAN_SCALAR * Coord.GaussianDistribution(euclideanDistance, 0, 1);
                        _IncrementHeatmapIfPossible(localHeatmap, currentCoord, -(float)heat);
                    }
                }
            }
        }

        private void _IncrementHeatmapIfPossible(float[,] heatmap, Coord idx, float amount) {
            if (idx.x >= 0 && idx.x < heatmap.GetLength(0) &&
                idx.y >= 0 && idx.y < heatmap.GetLength(1)) {
                heatmap[idx.x, idx.y] += amount;
            }
        }
        #endregion

        #region Task Assignment
        public bool RequestTask(out Task nextTask) {
            if (GlobalTaskQueue.Count == 0) {
                nextTask = null;
                return false;
            } else {
                nextTask = GlobalTaskQueue.Dequeue();
                UIInfoManager.instance.UILog/*Debug.Log*/(string.Format("[GlobalGrid] task={0} is assigned to robot, {1} tasks left to be assigned", 
                    nextTask.targetPos.ToString(), GlobalTaskQueue.Count.ToString()));

                // update UI
                UIInfoManager.instance.RenderTaskInfo(GlobalTaskQueue.Count, finishedTaskCount);

                return true;
            }
        }

        public void ReportTaskCompletion() {
            finishedTaskCount++;

            // update UI
            UIInfoManager.instance.RenderTaskInfo(GlobalTaskQueue.Count, finishedTaskCount);
        }
        #endregion

        #region Populate Grid with Code
        private void _FillRectangleInGridMap(int xLowLeft, int yLowLeft, 
                                             int xOffset, int yOffset,
                                             MapUnitEntity.MapUnitType type) {
            if (xOffset <= 0 || yOffset <= 0) {
                Debug.LogError("[GlobalGrid] offset should > 0");
                return;
            }
            // error check skipped, we assume the specified rectangle is contained in the grid map

            for (int xDelta = 0; xDelta < xOffset; xDelta++) {
                for (int yDelta = 0; yDelta < yOffset; yDelta++) {
                    this.gridMap[xLowLeft + xDelta, yLowLeft + yDelta].type = type;
                }
            }
        }

        /// <summary>
        /// Dimension: 20 * 15
        ///     TODO: Dimension 76 * 48
        /// </summary>
        public void Populate_debug_v1() {
            dimX = 20;
            dimY = 15;
            gridMap = new MapUnitEntity[dimX, dimY];
            gridRobot = new RobotEntity[dimX, dimY];

            // init
            for (int x = 0; x < dimX; x++) {
                for (int y = 0; y < dimY; y++) {
                    this.gridMap[x, y] = new MapUnitEntity(MapUnitEntity.MapUnitType.PUBLIC_ROAD);
                    this.gridRobot[x, y] = new RobotEntity(RobotEntity.RobotType.NONE, new Coord(x, y));
                }
            }

            // set grid map
            _FillRectangleInGridMap(0, 0, dimX, 1, MapUnitEntity.MapUnitType.BARRIER);
            _FillRectangleInGridMap(0, dimY - 1, dimX, 1, MapUnitEntity.MapUnitType.BARRIER);
            _FillRectangleInGridMap(0, 0, 1, dimY, MapUnitEntity.MapUnitType.BARRIER);
            _FillRectangleInGridMap(dimX - 1, 0, 1, dimY, MapUnitEntity.MapUnitType.BARRIER);

            _FillRectangleInGridMap(3, 3, 4, 9, MapUnitEntity.MapUnitType.GOODS_AREA);
            _FillRectangleInGridMap(3 + 1, 3 + 1, 4 - 2, 9 - 2, MapUnitEntity.MapUnitType.GOODS_SHELF);

            _FillRectangleInGridMap(3 + 4 + 2, 3, 4, 9, MapUnitEntity.MapUnitType.GOODS_AREA);
            _FillRectangleInGridMap(3 + 4 + 2 + 1, 3 + 1, 4 - 2, 9 - 2, MapUnitEntity.MapUnitType.GOODS_SHELF);

            _FillRectangleInGridMap(16, 2, 3, 11, MapUnitEntity.MapUnitType.CONVEYOR);

            // set gird robot
            //var xFetchRobotCoord = new int[] { 1, 1, 1, 7, 7, 7, 13, 13, 13 };
            //var yFetchRobotCoord = new int[] { 11, 8, 3, 11, 8, 3, 11, 8, 3 };
            //for (int i = 0; i < xFetchRobotCoord.Length; i++) {
            //    gridRobot[xFetchRobotCoord[i], yFetchRobotCoord[i]].type = RobotEntity.RobotType.FETCH;
            //}

            //var xFreightRobotCoord = new int[] { 2, 2, 2, 8, 8, 8, 15, 15, 15 };
            //var yFreightRobotCoord = new int[] { 11, 8, 3, 11, 8, 3, 11, 8, 3 };
            //for (int i = 0; i < xFreightRobotCoord.Length; i++) {
            //    gridRobot[xFreightRobotCoord[i], yFreightRobotCoord[i]].type = RobotEntity.RobotType.FREIGHT;
            //}
        }
        #endregion

        #region Populate Grid with Json
        public void PopulateMapWithJson(string filename) {

            int barrierSlotCount = 0;
            int roadSlotCount = 0;

            string path = Path.Combine(Application.dataPath, "Convertor", "json", filename + ".json");
            if (!File.Exists(path)) {
                UIInfoManager.instance.UILogError/*Debug.LogError*/("[GlobalGrid] PopulateMapWithJson : file not found");
                return;
            }
            string gridMapJson = File.ReadAllText(path);
            int[,] gridMapIntArr = JsonConvert.DeserializeObject<int[,]>(gridMapJson);

            dimX = gridMapIntArr.GetLength(0);
            dimY = gridMapIntArr.GetLength(1);
            gridMap = new MapUnitEntity[dimX, dimY];
            gridRobot = new RobotEntity[dimX, dimY];
            globalHeatmap = new float[dimX, dimY];  //automatically init to all 0

            // init
            for (int x = 0; x < dimX; x++) {
                for (int y = 0; y < dimY; y++) {
                    this.gridMap[x, y] = new MapUnitEntity(MapUnitEntity.MapUnitType.PUBLIC_ROAD);
                    this.gridRobot[x, y] = new RobotEntity(RobotEntity.RobotType.NONE, new Coord(x, y));
                }
            }

            for (int i = 0; i < dimX; i++) {
                for (int j = 0; j < dimY; j++) {
                    int entry = gridMapIntArr[i, j];
                    gridMap[i, j].type = int2MapUnitType[entry];

                    // count map info
                    if (gridMap[i, j].type == MapUnitEntity.MapUnitType.BARRIER)
                        barrierSlotCount++;
                    else if (gridMap[i, j].type == MapUnitEntity.MapUnitType.PUBLIC_ROAD)
                        roadSlotCount++;
                }
            }

            UIInfoManager.instance.UILog/*Debug.Log*/(string.Format("[GlobalGrid] json map loaded with dimension [dimX, dimY] = [{0}, {1}]. " +
                "Barrier count = {2}; Road count = {3}", 
                dimX.ToString(), dimY.ToString(), barrierSlotCount.ToString(), roadSlotCount.ToString()));

            // update UI
            UIInfoManager.instance.RenderMapSize(dimX, dimY);
        }

        public void PopulateRobotWithJson(string filename) {
            if (gridRobot == null || 
                gridRobot.GetLength(0) != dimX || gridRobot.GetLength(1) != dimY) {
                Debug.LogError("[GlobalGrid] PopulateRobotWithJson called but gridRobot not initialized yet");
                return;
            }

            string path = Path.Combine(Application.dataPath, "Convertor", "json", filename + ".json");
            if (!File.Exists(path)) {
                UIInfoManager.instance.UILogError/*Debug.LogError*/("[GlobalGrid] PopulateRobotWithJson : file not found");
                return;
            }
            string arrOfPosJson = File.ReadAllText(path);
            int[][] arrOfPos = JsonConvert.DeserializeObject<int[][]>(arrOfPosJson);

            int robotPriority = 1;
            foreach(int[] pos in arrOfPos) {
                // `new RobotEntity` here, instead of just change the `RobotType`
                gridRobot[pos[0], pos[1]] = new FreightRobot(new Coord(pos[0], pos[1]), robotPriority);
                robotPriority++;
            }
            UIInfoManager.instance.UILog/*Debug.Log*/("[GlobalGrid] robots init with json successfully");

            // update UI
            UIInfoManager.instance.RenderRobotCount(arrOfPos.Length);
        }

        public void PopulateTaskQueueWithJson(string filename) {
            if (GlobalTaskQueue == null) {
                Debug.LogError("[GlobalGrid] PopulateTaskQueueWithJson called but `GlobalTaskQueue` not initialized yet");
                return;
            }
            //GlobalTaskQueue.Enqueue(new Task(2, 6));
            //GlobalTaskQueue.Enqueue(new Task(12, 11));
            //GlobalTaskQueue.Enqueue(new Task(16, 3));

            string path = Path.Combine(Application.dataPath, "Convertor", "task_set", filename + ".json");
            if (!File.Exists(path)) {
                UIInfoManager.instance.UILogError/*Debug.LogError*/("[GlobalGrid] PopulateTaskQueueWithJson : file not found");
                return;
            }
            string arrOfTaskJson = File.ReadAllText(path);
            int[][] arrOfTask = JsonConvert.DeserializeObject<int[][]>(arrOfTaskJson);

            foreach (int[] task in arrOfTask) {
                if (!gridMap[task[0], task[1]].canEnter) {
                    Debug.LogError("[GlobalGrid] invalid task set input");
                    return;
                }
                GlobalTaskQueue.Enqueue(new Task(task[0], task[1]));
            }
            UIInfoManager.instance.UILog/*Debug.Log*/(string.Format("[GlobalGrid] global task queue init with json successfully, total task count = {0}", arrOfTask.Length));

            // update UI
            UIInfoManager.instance.RenderTotalTaskCount(arrOfTask.Length);
        }
        #endregion
    }
}