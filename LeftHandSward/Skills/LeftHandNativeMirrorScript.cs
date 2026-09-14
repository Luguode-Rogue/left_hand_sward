using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace LeftHandSward.Skills
{
    [ScriptComponentParams("LeftHandNativeMirrorScript")]
    public sealed class LeftHandNativeMirrorScript : ScriptComponentBehavior
    {
        private Skeleton _skeleton;
        private sbyte[] _rightChain;
        private sbyte[] _leftChain;
        private Mat3[] _rightIdleLocal;
        private Mat3 _leftItemIdleLocal;
        private bool _errorLogged;

        public bool MirrorActive { get; set; }

        public bool HasRightItemFrame { get; private set; }
        public bool HasLeftItemFrame { get; private set; }

        public MatrixFrame LastRightItemFrame { get; private set; }
        public MatrixFrame LastLeftItemFrame { get; private set; }

        internal void Configure(
            Skeleton skeleton,
            sbyte[] rightChain,
            sbyte[] leftChain,
            Mat3[] rightIdleLocal,
            Mat3 leftItemIdleLocal)
        {
            _skeleton = skeleton;
            _rightChain = rightChain;
            _leftChain = leftChain;
            _rightIdleLocal = rightIdleLocal;
            _leftItemIdleLocal = leftItemIdleLocal;
            _errorLogged = false;
            HasLeftItemFrame = false;
        }

        protected override bool SkeletonPostIntegrateCallback(
            AnimResult animResult)
        {
            if (_skeleton == null ||
                !_skeleton.IsValid ||
                _rightChain == null ||
                _leftChain == null ||
                _rightIdleLocal == null ||
                _rightChain.Length < 2 ||
                _rightChain.Length != _leftChain.Length ||
                _rightIdleLocal.Length != _rightChain.Length)
            {
                return false;
            }

            try
            {
                int last = _rightChain.Length - 1;

                // Always keep the current native right item frame. Once a
                // post-integrate callback is enabled the engine may stop syncing
                // weapon entities automatically, so the MissionBehavior uses this
                // frame while mirror mode is off.
                Transformation nativeRightItem =
                    animResult.GetEntitialOutTransform(
                        _rightChain[last],
                        _skeleton);
                LastRightItemFrame =
                    new MatrixFrame(
                        nativeRightItem.Rotation,
                        nativeRightItem.Origin);
                HasRightItemFrame = true;

                if (!MirrorActive)
                {
                    HasLeftItemFrame = false;
                    return false;
                }

                // Snapshot the native RH attack pose before touching either arm.
                Mat3[] rightAttackLocal =
                    new Mat3[_rightChain.Length];

                for (int i = 0; i < _rightChain.Length; i++)
                {
                    rightAttackLocal[i] =
                        GetBoneLocalRotation(
                            animResult,
                            _rightChain[i]);
                }

                // Mirror the original right-hand attack onto the anatomical left
                // arm. The final item bone is kept in its native left-hand grip
                // orientation; its parents carry the mirrored motion.
                for (int i = 0; i < last; i++)
                {
                    animResult.SetOutQuat(
                        _leftChain[i],
                        MirrorSagittal(rightAttackLocal[i]),
                        _skeleton);
                }

                animResult.SetOutQuat(
                    _leftChain[last],
                    _leftItemIdleLocal,
                    _skeleton);

                Transformation leftItem =
                    animResult.GetEntitialOutTransform(
                        _leftChain[last],
                        _skeleton);
                LastLeftItemFrame =
                    new MatrixFrame(
                        leftItem.Rotation,
                        leftItem.Origin);
                HasLeftItemFrame = true;

                // This is a left-only attack: restore the anatomical right arm to
                // the pose captured immediately before the skill started.
                for (int i = 0; i < _rightChain.Length; i++)
                {
                    animResult.SetOutQuat(
                        _rightChain[i],
                        _rightIdleLocal[i],
                        _skeleton);
                }

                Transformation resetRightItem =
                    animResult.GetEntitialOutTransform(
                        _rightChain[last],
                        _skeleton);
                LastRightItemFrame =
                    new MatrixFrame(
                        resetRightItem.Rotation,
                        resetRightItem.Origin);

                return true;
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    LeftHandSwardLog.Exception(
                        "LeftHandNativeMirror",
                        ex);
                }
                return false;
            }
        }

        private Mat3 GetBoneLocalRotation(
            AnimResult animResult,
            sbyte bone)
        {
            Transformation child =
                animResult.GetEntitialOutTransform(
                    bone,
                    _skeleton);
            sbyte parent =
                _skeleton.GetParentBoneIndex(bone);

            if (parent < 0)
                return child.Rotation;

            Transformation parentTransform =
                animResult.GetEntitialOutTransform(
                    parent,
                    _skeleton);

            return parentTransform.Rotation.TransformToLocal(
                in child.Rotation);
        }

        private static Mat3 MirrorSagittal(Mat3 rotation)
        {
            // Bannerlord human skeleton local/body convention: the tested
            // sagittal mirror for 1H slash motion is Z reflection,
            // M * R * M with M = diag(1,1,-1).
            return new Mat3(
                new Vec3(
                    rotation.s.x,
                    rotation.s.y,
                    -rotation.s.z),
                new Vec3(
                    rotation.f.x,
                    rotation.f.y,
                    -rotation.f.z),
                new Vec3(
                    -rotation.u.x,
                    -rotation.u.y,
                    rotation.u.z));
        }
    }
}
