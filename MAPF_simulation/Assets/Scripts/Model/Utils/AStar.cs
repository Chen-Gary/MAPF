﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Priority_Queue;

namespace MAPF.Utils {
    /// <summary>
    /// ref: https://www.redblobgames.com/pathfinding/a-star/introduction.html
    /// </summary>
    public class AStar {

        #region Inner Class & Struct
        private class Node {
            public Coord pos;   //should be consistent with index in `AStar.graph`
            public bool canEnter;   //should be expend?
            public float costToEnter;

            public Node(Coord pos_, bool canEnter_, float costToEnter_) {
                pos = pos_;
                canEnter = canEnter_;
                costToEnter = costToEnter_;
            }
        }
        #endregion

        #region Const
        private const float DEFAULT_COST = 1f;
        #endregion

        private Node[,] graph;

        public AStar(MapUnitEntity[,] gridMap, float[,] heatmap) {
            if (gridMap.GetLength(0) != heatmap.GetLength(0) ||
                gridMap.GetLength(1) != heatmap.GetLength(1)) {
                Debug.LogError("[AStar] dimension of gridMap and heatmap is not consistent");
                return;
            }

            int dimX = gridMap.GetLength(0);
            int dimY = gridMap.GetLength(1);
            graph = new Node[dimX, dimY];
            
            for (int x = 0; x < dimX; x++) {
                for (int y = 0; y < dimY; y++) {
                    bool canEnter = gridMap[x, y].canEnter;
                    Node node = new Node(new Coord(x, y), canEnter, DEFAULT_COST + heatmap[x, y]);
                    graph[x, y] = node;
                }
            }
        }

        public List<Coord> FindPath(Coord startPos, Coord goalPos) {
            Node start = graph[startPos.x, startPos.y];
            Node goal = graph[goalPos.x, goalPos.y];

            // smaller priority value, earlier get dequeued
            SimplePriorityQueue<Node, float> frontier = new SimplePriorityQueue<Node, float>();
            frontier.Enqueue(start, 0);

            // given a node, find the previous node which goes to this node
            Dictionary<Node/*current node*/, Node/*previous node*/> came_from = new Dictionary<Node, Node>();
            Dictionary<Node, float> cost_so_far = new Dictionary<Node, float>();

            came_from.Add(start, null);
            cost_so_far.Add(start, 0);

            while (frontier.Count > 0) {
                Node current = frontier.Dequeue();

                if (current == goal) 
                    break;

                List<Node> neighbors = _GetNeighbors(current.pos);
                foreach (Node next in neighbors) {
                    float new_cost = cost_so_far[current] + next.costToEnter;   //use heatmap
                    //float new_cost = cost_so_far[current] + DEFAULT_COST;     //do not use heatmap
                    if (!cost_so_far.ContainsKey(next) || new_cost < cost_so_far[next]) {
                        cost_so_far[next] = new_cost;   //add new or update
                        float priority = new_cost + heuristic(goal, next);
                        frontier.Enqueue(next, priority);
                        came_from[next] = current;      //add new or update
                    }
                }
            }

            // extract path from `came_from`
            List<Coord> path = new List<Coord>();
            Node bufferNode = goal;
            while (bufferNode != start) {
                path.Add(bufferNode.pos);
                if (!came_from.TryGetValue(bufferNode, out bufferNode)) {
                    Debug.Log("[AStar] AStar fails to find the path");
                    return null;
                }
            }
            //path.Add(start.pos);  //`startPos` is not included in the path
            path.Reverse();

            // debug: print path
            //string pathStr = "[Astar] path: ";
            //foreach (Coord coord in path) {
            //    pathStr += coord.ToString() + " -> ";
            //}
            //Debug.Log(pathStr);

            return path;
        }

        /// <summary>
        /// Manhattan distance
        /// </summary>
        private float heuristic(Node src, Node dst) {
            int manhattan_distance = Coord.ManhattanDistance(src.pos, dst.pos);
            return (float)manhattan_distance;
        }

        private List<Node> _GetNeighbors(Coord nodePos) {
            Coord[] deltaCoords = new Coord[] {
                new Coord( 1,  0),
                new Coord(-1,  0),
                new Coord( 0,  1),
                new Coord( 0, -1),
            };

            List<Node> neighbors = new List<Node>();

            foreach (Coord deltaCoord in deltaCoords) {
                Node node;
                if (_TryGetNode(nodePos + deltaCoord, out node)) {
                    if (node.canEnter) {
                        neighbors.Add(node);
                    }
                }
            }
            return neighbors;
        }

        private bool _TryGetNode(Coord nodePos, out Node node) {
            if (nodePos.x >= 0 && nodePos.x < graph.GetLength(0) &&
                nodePos.y >= 0 && nodePos.y < graph.GetLength(1)) {
                node = graph[nodePos.x, nodePos.y];
                return true;
            }
            node = null;
            return false;
        }
    }
}