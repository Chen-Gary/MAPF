﻿using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using MAPF.Utils;


namespace MAPF {
    /// <summary>
    /// Cloud in the cloud-edge-terminal architecture
    /// 全局控制中心
    /// </summary>
    public class GlobalGrid {

        public static GlobalGrid _instance;     //singleton

        public MapUnitEntity[,] gridMap;
        public RobotEntity[,] gridRobot;

        public int dimX = 0;
        public int dimY = 0;

        Queue<Task> GlobalTaskQueue;

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

            InitTaskQueue();
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
            if (gridRobot[nextPos.x, nextPos.y].type != RobotEntity.RobotType.NONE) {
                Debug.LogWarning(string.Format("[GlobalGrid] the position intended to move {0} to is already occupied", nextPos.ToString()));
                return false;
            }
            if (!gridMap[nextPos.x, nextPos.y].canEnter) {
                Debug.LogError("[GlobalGrid] the position intended to move to cannot be entered");
                return false;
            }

            // update `gridRobot`
            gridRobot[currentPos.x, currentPos.y] = new RobotEntity(RobotEntity.RobotType.NONE, 
                                                                    new Coord(currentPos.x, currentPos.y));
            gridRobot[nextPos.x, nextPos.y] = robot;
            return true;
        }
        #endregion

        #region Task Assignment
        private void InitTaskQueue() {
            GlobalTaskQueue = new Queue<Task>();

            // TODO: randomly generate tasks
            GlobalTaskQueue.Enqueue(new Task(2, 6));
        }

        public bool RequestTask(out Task nextTask) {
            if (GlobalTaskQueue.Count == 0) {
                nextTask = null;
                return false;
            } else {
                nextTask = GlobalTaskQueue.Dequeue();
                Debug.Log(string.Format("[GlobalGrid] task[{0}] is assigned to robot", nextTask.ToString()));
                return true;
            }
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
            Dictionary<int, MapUnitEntity.MapUnitType> int2MapUnitType = 
                new Dictionary<int, MapUnitEntity.MapUnitType>() {
                    { 0, MapUnitEntity.MapUnitType.PUBLIC_ROAD },
                    { 1, MapUnitEntity.MapUnitType.BARRIER }
                };

            string path = Path.Combine(Application.dataPath, "Convertor", "json", filename + ".json");
            if (!File.Exists(path)) {
                Debug.LogError("[GlobalGrid] PopulateWithJson : file not found");
                return;
            }
            string gridMapJson = File.ReadAllText(path);
            int[,] gridMapIntArr = JsonConvert.DeserializeObject<int[,]>(gridMapJson);

            dimX = gridMapIntArr.GetLength(0);
            dimY = gridMapIntArr.GetLength(1);
            gridMap = new MapUnitEntity[dimX, dimY];
            gridRobot = new RobotEntity[dimX, dimY];

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
                }
            }

            Debug.Log(string.Format("[GlobalGrid] json map loaded with dimension [dimX, dimY] = [{0}, {1}]", 
                dimX.ToString(), dimY.ToString()));
        }

        public void PopulateRobotWithJson(string filename) {
            if (gridRobot == null || 
                gridRobot.GetLength(0) != dimX || gridRobot.GetLength(1) != dimY) {
                Debug.LogError("[GlobalGrid] PopulateRobotWithJson called but gridRobot not initialized yet");
            }

            string path = Path.Combine(Application.dataPath, "Convertor", "json", filename + ".json");
            string arrOfPosJson = File.ReadAllText(path);
            int[][] arrOfPos = JsonConvert.DeserializeObject<int[][]>(arrOfPosJson);

            int robotPriority = 1;
            foreach(int[] pos in arrOfPos) {
                // `new RobotEntity` here, instead of just change the `RobotType`
                gridRobot[pos[0], pos[1]] = new FreightRobot(new Coord(pos[0], pos[1]), robotPriority);
                robotPriority++;
            }
        }
        #endregion
    }
}