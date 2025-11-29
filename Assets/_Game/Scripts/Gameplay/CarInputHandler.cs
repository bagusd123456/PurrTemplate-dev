using PurrNet.Packing;
using PurrNet.Prediction;
using System;
using UnityEngine;

public struct CarInputData : IPredictedData<CarInputData>
{
    public float Horizontal; // A/D
    public float Vertical;   // W/S
    public bool Handbrake;   // Space
    public void Dispose() { }
}

public class CarInputHandler
{
    [SerializeField]
    private PrometeoCarController carController;

    public struct State : IPredictedData<State>
    {
        // Logic State (Internal variables we need to save to keep movement smooth)
        public float SteeringAxis;
        public float ThrottleAxis;
        public float DriftingAxis;
        public bool IsDrifting;
        public bool IsTractionLocked;
        public bool DeceleratingCar; 

        public void Dispose()
        {
            // TODO release managed resources here
        }
    }
}
