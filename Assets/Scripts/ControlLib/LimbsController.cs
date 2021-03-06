﻿using UnityEngine;

public class LimbsController : CharaController {

    // IK parameters
    private Vector3 _ik_target;
    private static int ctrl_count = 0;
    private int ctrl_id;
    private Vector3[] velocity_filter;
    private Vector3 filtered_velocity = Vector3.zero;
    private const int filter_size = 32;
    private int filter_cursor = 0;

    // Constructor
    public LimbsController(CharaConfiguration chara, Configuration config, GameObject[] objs, bool debug = false)
        : base(chara, config, objs, debug) {
        ctrl_count = ctrl_count % 2;
        ctrl_id = ctrl_count++;
        velocity_filter = new Vector3[filter_size];
        for (int i = 0; i < filter_size; ++i)
            velocity_filter[i] = Vector3.zero;
    }

    public override void GenerateJointPositionTrajectory() {
        AnimMode mode = GetCurrentMode();

        // Assign IK target for swing or stance foot
        if (mode == AnimMode.kSwing) {
            _ik_target = _objs[0].transform.position + IPMError();// + FootError(_objs.Length - 1);
            _ik_target.y = _config.ground_offset + _config.kLiftH * PhaseManager.InterpolateHeight(Time.time, ctrl_id);
            if (_debug) {
                _config.gizmos.Add(_ik_target);
                _config.gizcolor.Add(Color.blue);
                _config.gizmos.Add(_objs[0].transform.position);
                _config.gizcolor.Add(Color.red);
            }
        } else {
            _ik_target = _objs[0].transform.position + FootError(_objs.Length - 1);
            _ik_target.y = _config.ground_offset;
            if (_debug) {
                _config.gizmos.Add(_ik_target);
                _config.gizcolor.Add(Color.blue);
            }
        }

        // 1st pass IK solving
        for (int i = 0; i < _objs.Length; ++i)
            _target_pos[i] = _objs[i].transform.position;
        FABRIKSolver.SolveIKWithVectorConstraint(ref _target_pos, _ik_target, _chara.root.transform.forward);

        // 2nd pass IK solving
        //if (GetCurrentMode() == AnimMode.kSwing)
            //_ik_target = _objs[_objs.Length - 1].transform.localToWorldMatrix.MultiplyPoint(_rigs[_objs.Length - 1].centerOfMass);
        //else
            _ik_target = _objs[0].transform.position;

        _ik_target += _config.kDV * _config.kV * Time.fixedDeltaTime;
        _ik_target.y = _config.ground_offset + _config.kDH;
        FABRIKSolver.SolveIKWithVectorConstraint(ref _target_pos, _ik_target, _chara.root.transform.forward, true);

        if (_debug) {
            foreach (Vector3 target in _target_pos) {
                _config.gizmos.Add(target);
                _config.gizcolor.Add(Color.yellow);
            }
        }
    }

    public override void GenerateJointRotation() {

        AnimMode mode = GetCurrentMode();

        for (int i = 0; i < _objs.Length; ++i) {

            // Foot joint
            if (i == _objs.Length - 1) {
                _target_rot[i] = Quaternion.Inverse(_joints[i].connectedBody.transform.rotation) *
                    Quaternion.LookRotation(Vector3.up, _joints[i].connectedBody.transform.up);
                break;
            }

            // Calculate local axis
            Vector3 x = _target_pos[i + 1] - _target_pos[i];
            if(x == Vector3.zero) {
                x = _config.kDV;
            }
            Vector3 y = Vector3.Cross(x, _chara.root.transform.forward);
            if (Vector3.Dot(y, _chara.root.transform.up) > 0)
                y = -y;
            if (y == Vector3.zero)
                y = -_chara.root.transform.up;
            Vector3 z = Vector3.Cross(y, x).normalized;

            // Stance foot balance
            Quaternion local = Quaternion.Inverse(_joints[i].connectedBody.transform.rotation);
            Quaternion joint = Quaternion.LookRotation(z, y);
            if (mode != AnimMode.kSwing && i == 0) {
                _target_rot[i] = local * joint;

                Vector3 root_err = Quaternion.FromToRotation(_chara.root.transform.right, -Vector3.up).eulerAngles.Shift2Pi();
                _target_rot[i] *= Quaternion.Euler(new Vector3(0, root_err[0], -root_err[2]));
            }
            // other joints
            else {
                _target_rot[i] = local * joint;
            }
        }
    }

    public override void ApplyJointRotation() {
        base.ApplyJointRotation();
    }

    public override AnimMode GetCurrentMode() {
        // return AnimMode.kStance;
        return PhaseManager.GetCurrentPhase(Time.time, ctrl_id);
    }

    private Vector3 IPMError() {
        Vector3 d = _chara.root.velocity;
        filtered_velocity = (filtered_velocity * filter_size - velocity_filter[filter_cursor] + d) / filter_size;
        d = filtered_velocity;
        velocity_filter[filter_cursor] = d;
        filter_cursor = (filter_cursor + 1) % filter_size;
        //Vector3 com = _chara.GetCenterOfMass();
        Vector3 com = _chara.root.transform.position;
        com.y -= _config.ground_offset;

        float g = Mathf.Abs(Physics.gravity.y);

        d *= Mathf.Sqrt(com.y / g + Vector3.SqrMagnitude(d) / (4 * g * g));
        d -= _config.kVAlpha * _config.kDV;

        return d;
    }

    private Vector3 FootError(int index) {
        Vector3 com_in_world = _objs[index].transform.localToWorldMatrix.MultiplyPoint(_rigs[index].centerOfMass);
        return _objs[index].transform.position - com_in_world;
    }
}
