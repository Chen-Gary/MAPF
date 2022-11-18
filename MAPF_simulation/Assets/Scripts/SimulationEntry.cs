﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MAPF.UI;
using MAPF.View;

namespace MAPF { 
    public class SimulationEntry : MonoBehaviour {
        [SerializeField]
        private UIInfoManager _uiInfoManager = null;
        [SerializeField]
        private GlobalGridView _globalGridView = null;

        private GlobalGrid m_globalGrid;

        #region Unity Callbacks
        private void Start() {
            // construct `m_globalGrid`
            m_globalGrid = new GlobalGrid();
            //m_globalGrid.Populate_debug_v1();
            m_globalGrid.PopulateMapWithJson("Map1");
            m_globalGrid.PopulateRobotWithJson("Bot1");

            // render
            _globalGridView.Render(m_globalGrid);
            _uiInfoManager.Render(0);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.D)) {
                _uiInfoManager.Render(1);

                // for all robot
                List<FreightRobot> robots = new List<FreightRobot>();
                for (int x = 0; x < m_globalGrid.dimX; x++) {
                    for (int y = 0; y < m_globalGrid.dimY; y++) {
                        if (m_globalGrid.gridRobot[x, y].type == RobotEntity.RobotType.FREIGHT)
                            robots.Add((FreightRobot) m_globalGrid.gridRobot[x, y]);
                    }
                }
                for (int i = 0; i < robots.Count; i++) {
                    robots[i].Operate();
                }

                // rerender view
                _globalGridView.Render(m_globalGrid);
            }
        }
        #endregion
    }
}