using System;
using FarseerGames.FarseerPhysics.Interfaces;
using FarseerGames.FarseerPhysics.Mathematics;
using Microsoft.Xna.Framework;

namespace FarseerGames.FarseerPhysics.Dynamics
{
    /// <summary>
    /// <para>
    /// Body is at the core of the Rigid Body System. 
    /// <para>
    /// The Body handles all the physics of motion: position, velocity, acceleration
    /// forces, torques, etc...</para>
    /// <para>The Body is integrated every timestep (which should be fixed) by the <see cref="PhysicsSimulator">PhysicsSimulator</see> in the following manner:
    /// Set forces and torques (gravity, springs, user input...)->Apply forces and torques (updating velocity only)->Update positions and Rotations</para>
    /// <para>In technical terms, this is known as Symplectic Euler Integration because
    /// velocity is updated prior to position (The reverse of simple Euler Integration)</para>
    /// </summary>
    public class Body : IIsDisposable
    {
        #region Delegates

        public delegate void UpdatedEventHandler(ref Vector2 position, ref float rotation);

        #endregion

        private bool _isQuadraticDragEnabled;
        private float _linearDragCoefficient = .001f; //tuned for a body of mass 1
        internal float _mass = 1;
        private float _momentOfInertia = 1; //1 unit square ;
        private float _previousAngularVelocity;
        private Vector2 _previousLinearVelocity = Vector2.Zero;
        private Vector2 _previousPosition = Vector2.Zero;
        private float _previousRotation;
        private float _quadraticDragCoefficient = .001f;
        private float _rotationalDragCoefficient = .001f; //tuned for a 1.28m X 1.28m rectangle with mass = 1
        private Vector2 _tempVelocity = Vector2.Zero;
        private float _torque;

        internal float angularVelocity;
        internal float angularVelocityBias;
        internal bool enabled = true;
        internal Vector2 force = Vector2.Zero;
        internal bool ignoreGravity;
        internal Vector2 impulse = Vector2.Zero;
        internal float inverseMass = 1;
        internal float inverseMomentOfInertia = 1;
        protected bool isDisposed;
        internal bool isStatic;
        internal Vector2 linearVelocity = Vector2.Zero;
        internal Vector2 linearVelocityBias = Vector2.Zero;
        internal Vector2 position = Vector2.Zero;
        internal float rotation;
        //shouldn't need this. commenting out but keeping incase needed in the future.
        //private float linearDragVelocityThreshhold = .000001f;

        public UpdatedEventHandler Updated;
        //Note: Cleanup. Never used
        //private Vector2 velocityTemp = Vector2.Zero;

        public Body()
        {
        }

        /// <summary>
        /// Body constructor that makes a copy of another body
        /// </summary>
        /// <param name="body"></param>
        public Body(Body body)
        {
            Mass = body.Mass;
            MomentOfInertia = body.MomentOfInertia;
            LinearDragCoefficient = body.LinearDragCoefficient;
            RotationalDragCoefficient = body.RotationalDragCoefficient;
        }

        #region Added by Daniel Pramel 08/17/08

        /// <summary>
        /// Returns or sets how long (ms) the body is below the MinimumVelocity.
        /// If this time is greater than the InactivityControllers "MaxIdleTime", it will be deactivated
        /// </summary>
        public float IdleTime;

        /// <summary>
        /// Returns or sets whether the body can be deactivated by the InactivityController or not
        /// </summary>
        public bool IsAutoIdle;

        /// <summary>
        /// Returns or sets the minimum velocity. If the bodys velocity is below this value, it can
        /// be deactivated
        /// </summary>
        public float MinimumVelocity = 55;

        /// <summary>
        /// Returns whether the body is below the minimum velocity or not
        /// </summary>
        public bool Moves
        {
            get { return linearVelocity.Length() >= 55; }
        }

        #endregion

        /// <summary>
        /// Determines if this body is enabled or not. This value will be used by the physics simulator 
        /// to determine whether or not to update this body
        /// </summary>
        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        /// <summary>
        /// The mass of the Body
        /// </summary>
        public float Mass
        {
            get { return _mass; }
            set
            {
                if (value == 0)
                {
                    throw new Exception("Mass cannot be 0");
                }
                _mass = value;
                if (isStatic)
                {
                    inverseMass = 0;
                }
                else
                {
                    inverseMass = 1f/value;
                }
            }
        }

        /// <summary>
        /// The inverse of the mass of the body (1/Mass)
        /// </summary>
        public float InverseMass
        {
            get { return inverseMass; }
        }

        /// <summary>
        /// The moment of inertia of the body. 
        /// <para>The moment of intertia of a body in 2d is a scalar value that represents how
        /// difficult (or not difficult) it is to rotate a body about the center of mass.</para>
        /// <para>The moment of inertia is varies by the shape of the body.  For basic shapes like
        /// circles and rectangles, forumulas exist for computing the moment of interia based on
        /// the shape properties (radius of the circle, or length and width of the rectangle)</para>
        /// <para>For bodies that are not basic, it is usually good enough to estimate the moment of
        /// intertia by picking a basic shape that is close to the same shape. It is also possible
        /// using more advance calculus techniques to compute the actual moment of intertia of 
        /// non-basic shapes.</para>
        /// </summary>
        public float MomentOfInertia
        {
            get { return _momentOfInertia; }
            set
            {
                if (value == 0)
                {
                    throw new Exception("Mass cannot be 0");
                }
                _momentOfInertia = value;
                if (isStatic)
                {
                    inverseMomentOfInertia = 0;
                }
                else
                {
                    inverseMomentOfInertia = 1f/value;
                }
            }
        }

        /// <summary>
        /// The inverse of the moment of inertia of the body (1/MomentOfInertia)
        /// </summary>
        public float InverseMomentOfInertia
        {
            get { return inverseMomentOfInertia; }
        }

        /// <summary>
        /// Indicates this body is fixed within the world and will not move no matter what forces are applied.
        /// <para>Bodies that represent land or world borders are a good examples of static bodies.</para>
        /// </summary>
        public bool IsStatic
        {
            get { return isStatic; }
            set
            {
                isStatic = value;
                if (isStatic)
                {
                    inverseMass = 0;
                    inverseMomentOfInertia = 0;
                }
                else
                {
                    inverseMass = 1f/_mass;
                    inverseMomentOfInertia = 1f/_momentOfInertia;
                }
            }
        }

        /// <summary>
        /// Linear drag can be thought of as "air drag". The LinearDragCoefficient controls how much linear (non-rotational) drag
        /// a body is subject to. 
        /// <para>Linear drag causes a force to be applied to the body always in the direction opposite its velocity vector.
        /// The magnitude of this force is the speed of the body squared multiplied by its LinearDragCoefficent. 
        /// <c>force = velocity*velocity*LinearDragCoeficcent</c></para>
        /// </summary>
        public float LinearDragCoefficient
        {
            get { return _linearDragCoefficient; }
            set { _linearDragCoefficient = value; }
        }

        public float QuadraticeDragCoeficient
        {
            get { return _quadraticDragCoefficient; }
            set { _quadraticDragCoefficient = value; }
        }

        public bool IsQuadraticDragEnabled
        {
            get { return _isQuadraticDragEnabled; }
            set { _isQuadraticDragEnabled = value; }
        }

        public float RotationalDragCoefficient
        {
            get { return _rotationalDragCoefficient; }
            set { _rotationalDragCoefficient = value; }
        }

        public Vector2 Position
        {
            get { return position; }
            set
            {
                position = value;
                if (Updated != null)
                {
                    Updated(ref position, ref rotation);
                }
            }
        }

        public int Revolutions { get; private set; }

        public float Rotation
        {
            get { return rotation; }
            set
            {
                rotation = value;
                while (rotation > MathHelper.TwoPi)
                {
                    rotation -= MathHelper.TwoPi;
                    ++Revolutions;
                }
                while (rotation <= 0)
                {
                    rotation += MathHelper.TwoPi;
                    --Revolutions;
                }
                TotalRotation = rotation + Revolutions*MathHelper.TwoPi;

                if (Updated != null)
                {
                    Updated(ref position, ref rotation);
                }
            }
        }

        public float TotalRotation { get; internal set; }

        public Vector2 LinearVelocity
        {
            get { return linearVelocity; }
            set { linearVelocity = value; }
        }

        public float AngularVelocity
        {
            get { return angularVelocity; }
            set { angularVelocity = value; }
        }

        public Vector2 Force
        {
            get { return force; }
        }

        public float Torque
        {
            get { return _torque; }
        }

        public Object Tag { get; set; }

        public bool IgnoreGravity
        {
            get { return ignoreGravity; }
            set { ignoreGravity = value; }
        }

        public Vector2 XVectorInWorldCoordinates
        {
            get
            {
                //Matrix bodyMatrix = BodyMatrix;
                bodyMatrixTemp = GetBodyMatrix();
                return new Vector2(bodyMatrixTemp.Right.X, bodyMatrixTemp.Right.Y);
            }
        }

        public Vector2 YVectorInWorldCoordinates
        {
            get
            {
                //Matrix bodyMatrix = GetBodyMatrix();
                bodyMatrixTemp = GetBodyMatrix();
                return new Vector2(bodyMatrixTemp.Up.X, bodyMatrixTemp.Up.Y);
            }
        }

        #region GetWorldPosition variables

        private Vector2 worldPositionTemp = Vector2.Zero;

        #endregion

        #region IIsDisposable Members

        public bool IsDisposed
        {
            get { return isDisposed; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        public event EventHandler<EventArgs> Disposed;

        /// <summary>
        /// This method will reset position and motion properties. This method is useful
        /// when creating pools of re-usable bodies. It could be used when releasing bodies
        /// back into the pool.
        /// </summary>
        public void ResetDynamics()
        {
            linearVelocity.X = 0;
            linearVelocity.Y = 0;
            _previousLinearVelocity.X = 0;
            _previousLinearVelocity.Y = 0;
            angularVelocity = 0;
            _previousAngularVelocity = 0;
            position.X = 0;
            position.Y = 0;
            _previousPosition.X = 0;
            _previousPosition.Y = 0;
            rotation = 0;
            Revolutions = 0;
            TotalRotation = 0;
            force.X = 0;
            force.Y = 0;
            _torque = 0;
            impulse.X = 0;
            impulse.Y = 0;
            _linearDrag.X = 0;
            _linearDrag.Y = 0;
            _rotationalDrag = 0;
        }

        public Matrix GetBodyMatrix()
        {
            Matrix.CreateTranslation(position.X, position.Y, 0, out translationMatrixTemp);
            Matrix.CreateRotationZ(rotation, out rotationMatrixTemp);
            Matrix.Multiply(ref rotationMatrixTemp, ref translationMatrixTemp, out bodyMatrixTemp);
            return bodyMatrixTemp;
        }

        public void GetBodyMatrix(out Matrix bodyMatrix)
        {
            Matrix.CreateTranslation(position.X, position.Y, 0, out translationMatrixTemp);
            Matrix.CreateRotationZ(rotation, out rotationMatrixTemp);
            Matrix.Multiply(ref rotationMatrixTemp, ref translationMatrixTemp, out bodyMatrix);
        }

        public Matrix GetBodyRotationMatrix()
        {
            Matrix.CreateRotationZ(rotation, out rotationMatrixTemp);
            return rotationMatrixTemp;
        }

        public void GetBodyRotationMatrix(out Matrix rotationMatrix)
        {
            Matrix.CreateRotationZ(rotation, out rotationMatrix);
        }

        public Vector2 GetWorldPosition(Vector2 localPosition)
        {
            GetBodyMatrix(out bodyMatrixTemp);
            Vector2.Transform(ref localPosition, ref bodyMatrixTemp, out worldPositionTemp);
            return worldPositionTemp;
        }

        public void GetWorldPosition(ref Vector2 localPosition, out Vector2 worldPosition)
        {
            GetBodyMatrix(out bodyMatrixTemp);
            Vector2.Transform(ref localPosition, ref bodyMatrixTemp, out worldPosition);
        }

        public Vector2 GetLocalPosition(Vector2 worldPosition)
        {
            GetBodyRotationMatrix(out rotationMatrixTemp);
            Matrix.Transpose(ref rotationMatrixTemp, out rotationMatrixTemp);
            Vector2.Subtract(ref worldPosition, ref position, out localPositionTemp);
            Vector2.Transform(ref localPositionTemp, ref rotationMatrixTemp, out localPositionTemp);
            return localPositionTemp;
        }

        public void GetLocalPosition(ref Vector2 worldPosition, out Vector2 localPosition)
        {
            GetBodyRotationMatrix(out rotationMatrixTemp);
            Matrix.Transpose(ref rotationMatrixTemp, out rotationMatrixTemp);
            Vector2.Subtract(ref worldPosition, ref position, out localPosition);
            Vector2.Transform(ref localPosition, ref rotationMatrixTemp, out localPosition);
        }

        public Vector2 GetVelocityAtLocalPoint(Vector2 localPoint)
        {
            //Note: Cleanup. Never used
            //Vector2 velocity = linearVelocity +
            //                   Calculator.Cross(angularVelocity, (GetWorldPosition(localPoint) - Position));
            // angularVelocity * (GetWorldPosition(localPoint) - Position);
            GetVelocityAtLocalPoint(ref localPoint, out _tempVelocity);
            return _tempVelocity;
        }

        public void GetVelocityAtLocalPoint(ref Vector2 localPoint, out Vector2 velocity)
        {
            GetWorldPosition(ref localPoint, out r1);
            Vector2.Subtract(ref r1, ref position, out r1);
            GetVelocityAtWorldOffset(ref r1, out velocity);
        }

        public Vector2 GetVelocityAtWorldPoint(Vector2 worldPoint, Vector2 velocity)
        {
            GetVelocityAtWorldPoint(ref worldPoint, out velocity);
            return velocity;
        }

        public void GetVelocityAtWorldPoint(ref Vector2 worldPoint, out Vector2 velocity)
        {
            Vector2.Subtract(ref worldPoint, ref position, out r1);
            GetVelocityAtWorldOffset(ref r1, out velocity);
        }

        //for offset, think r1!

        public void GetVelocityAtWorldOffset(ref Vector2 offset, out Vector2 velocity)
        {
            #region INLINED: Calculator.Cross(ref angularVelocity, ref offset, out velocity);

            velocity.X = -angularVelocity*offset.Y;
            velocity.Y = angularVelocity*offset.X;

            #endregion

            #region INLINED: Vector2.Add(ref linearVelocity, ref velocity, out velocity);

            velocity.X = velocity.X + linearVelocity.X;
            velocity.Y = velocity.Y + linearVelocity.Y;

            #endregion
        }

        public void GetVelocityBiasAtWorldOffset(ref Vector2 offset, out Vector2 velocityBias)
        {
            #region INLINED: Calculator.Cross(ref angularVelocityBias, ref offset, out velocityBias);

            velocityBias.X = -angularVelocityBias*offset.Y;
            velocityBias.Y = angularVelocityBias*offset.X;

            #endregion

            #region INLINED: Vector2.Add(ref linearVelocityBias, ref velocityBias, out velocityBias);

            velocityBias.X = velocityBias.X + linearVelocityBias.X;
            velocityBias.Y = velocityBias.Y + linearVelocityBias.Y;

            #endregion
        }

        public void ApplyForce(Vector2 force)
        {
            #region INLINE:  Vector2.Add(ref this.force, ref force, out this.force);

            this.force.X = this.force.X + force.X;
            this.force.Y = this.force.Y + force.Y;

            #endregion
        }

        public void ApplyForce(ref Vector2 force)
        {
            #region INLINE: Vector2.Add(ref this.force, ref force, out this.force);

            this.force.X = this.force.X + force.X;
            this.force.Y = this.force.Y + force.Y;

            #endregion
        }

        public void ApplyForceAtLocalPoint(Vector2 force, Vector2 point)
        {
            //calculate torque (2D cross product point X force)
            GetWorldPosition(ref point, out diff);
            Vector2.Subtract(ref diff, ref position, out diff);

            float torque = diff.X*force.Y - diff.Y*force.X;

            //add to torque
            _torque += torque;

            // add linear force
            this.force.X += force.X;
            this.force.Y += force.Y;
        }

        public void ApplyForceAtLocalPoint(ref Vector2 force, ref Vector2 point)
        {
            //calculate torque (2D cross product point X force)
            GetWorldPosition(ref point, out diff);
            Vector2.Subtract(ref diff, ref position, out diff);

            float torque = diff.X*force.Y - diff.Y*force.X;

            //add to torque
            _torque += torque;

            // add linear force
            this.force.X += force.X;
            this.force.Y += force.Y;
        }

        public void ApplyForceAtWorldPoint(ref Vector2 force, ref Vector2 point)
        {
            Vector2.Subtract(ref point, ref position, out diff);

            float torque = diff.X*force.Y - diff.Y*force.X;

            //add to torque
            _torque += torque;

            // add linear force
            this.force.X += force.X;
            this.force.Y += force.Y;
        }

        public void ApplyForceAtWorldPoint(Vector2 force, Vector2 point)
        {
            Vector2.Subtract(ref point, ref position, out diff);

            float torque = diff.X*force.Y - diff.Y*force.X;

            //add to torque
            _torque += torque;

            // add linear force
            this.force.X += force.X;
            this.force.Y += force.Y;
        }

        public void ClearForce()
        {
            force.X = 0;
            force.Y = 0;
        }

        public void ApplyTorque(float torque)
        {
            _torque += torque;
        }

        public void ClearTorque()
        {
            _torque = 0;
        }

        /// <summary>
        /// Stores all applied impulses so that they can be applied at the same time
        /// by the PhysicsSimulator.
        /// </summary>
        /// <param name="impulse"></param>
        public void ApplyImpulse(Vector2 impulse)
        {
            #region INLINE: Vector2.Multiply(ref impulse, inverseMass, out dv);

            dv.X = impulse.X*inverseMass;
            dv.Y = impulse.Y*inverseMass;

            #endregion

            #region INLINE: Vector2.Add(ref dv, ref linearVelocity, out linearVelocity);

            this.impulse.X += dv.X + linearVelocity.X;
            this.impulse.Y += dv.Y + linearVelocity.Y;

            #endregion
        }

        /// <summary>
        /// Stores all applied impulses so that they can be applied at the same time
        /// by the PhysicsSimulator.
        /// </summary>
        /// <param name="impulse"></param>
        public void ApplyImpulse(ref Vector2 impulse)
        {
            #region INLINE: Vector2.Multiply(ref impulse, inverseMass, out dv);

            dv.X = impulse.X*inverseMass;
            dv.Y = impulse.Y*inverseMass;

            #endregion

            #region INLINE: Vector2.Add(ref dv, ref linearVelocity, out linearVelocity);

            this.impulse.X += dv.X + linearVelocity.X;
            this.impulse.Y += dv.Y + linearVelocity.Y;

            #endregion
        }

        /// <summary>
        /// Applys then clears all the external impulses that were accumulated
        /// </summary>
        internal void ApplyImpulses()
        {
            ApplyImmediateImpulse(ref impulse);
            impulse.X = 0;
            impulse.Y = 0;
        }

        /// <summary>
        /// used internally only by the joints and arbiter.
        /// </summary>
        /// <param name="impulse"></param>
        internal void ApplyImmediateImpulse(ref Vector2 impulse)
        {
            #region INLINE: Vector2.Multiply(ref impulse, inverseMass, out dv);

            dv.X = impulse.X*inverseMass;
            dv.Y = impulse.Y*inverseMass;

            #endregion

            #region INLINE: Vector2.Add(ref dv, ref linearVelocity, out linearVelocity);

            linearVelocity.X = dv.X + linearVelocity.X;
            linearVelocity.Y = dv.Y + linearVelocity.Y;

            #endregion
        }

        public void ClearImpulse()
        {
            impulse.X = 0;
            impulse.Y = 0;
        }

        public void ApplyAngularImpulse(float impulse)
        {
            angularVelocity += impulse*inverseMomentOfInertia;
        }

        public void ApplyAngularImpulse(ref float impulse)
        {
            angularVelocity += impulse*inverseMomentOfInertia;
        }

        private void ApplyDrag()
        {
            #region INLINE: speed = linearVelocity.Length();

            //requrired for quadratic drag. 
            if (_isQuadraticDragEnabled)
            {
                float num = (linearVelocity.X*linearVelocity.X) + (linearVelocity.Y*linearVelocity.Y);
                _speed = (float) Math.Sqrt(num);
                //Note: Cleanup. Never used.
                //Vector2 quadraticDrag = Vector2.Zero;
                //Vector2 totalDrag = Vector2.Zero;
            }

            #endregion

            //if (speed > linearDragVelocityThreshhold) {

            #region INLINE: Vector2.Multiply(ref linearVelocity, -linearDragCoefficient, out linearDrag);

            _linearDrag.X = -linearVelocity.X*_linearDragCoefficient;
            _linearDrag.Y = -linearVelocity.Y*_linearDragCoefficient;

            #endregion

            //quadratic drag for "fast" moving objects. Must be enabled first.
            if (_isQuadraticDragEnabled)
            {
                #region INLINE: Vector2.Multiply(ref linearVelocity, -quadraticDragCoefficient * speed, out quadraticDrag);

                _quadraticDrag.X = -_quadraticDragCoefficient*_speed*linearVelocity.X;
                _quadraticDrag.Y = -_quadraticDragCoefficient*_speed*linearVelocity.Y;

                #endregion

                #region INLINE: Vector2.Add(ref linearDrag, ref quadraticDrag, out totalDrag);

                _totalDrag.X = _linearDrag.X + _quadraticDrag.X;
                _totalDrag.Y = _linearDrag.Y + _quadraticDrag.Y;

                #endregion

                ApplyForce(ref _totalDrag);
            }
            else
            {
                ApplyForce(ref _linearDrag);
            }
            //}

            _rotationalDrag = angularVelocity*angularVelocity*Math.Sign(angularVelocity);
            _rotationalDrag *= -_rotationalDragCoefficient;
            ApplyTorque(_rotationalDrag);
        }

        internal void IntegrateVelocity(float dt)
        {
            if (isStatic)
            {
                return;
            }
            //linear
            ApplyDrag();

            #region INLINE: Vector2.Multiply(ref force, inverseMass, out acceleration);

            acceleration.X = force.X*inverseMass;
            acceleration.Y = force.Y*inverseMass;

            #endregion

            #region INLINE: Vector2.Multiply(ref acceleration, dt, out dv);

            dv.X = acceleration.X*dt;
            dv.Y = acceleration.Y*dt;

            #endregion

            _previousLinearVelocity = linearVelocity;

            #region INLINE: Vector2.Add(ref previousLinearVelocity, ref dv, out linearVelocity);

            linearVelocity.X = _previousLinearVelocity.X + dv.X;
            linearVelocity.Y = _previousLinearVelocity.Y + dv.Y;

            #endregion

            //angular
            dw = _torque*inverseMomentOfInertia*dt;
            _previousAngularVelocity = angularVelocity;
            angularVelocity = _previousAngularVelocity + dw;
        }

        internal void IntegratePosition(float dt)
        {
            if (isStatic)
            {
                return;
            }

            //linear

            #region INLINE: Vector2.Add(ref linearVelocity, ref linearVelocityBias, out bodylinearVelocity);

            bodylinearVelocity.X = linearVelocity.X + linearVelocityBias.X;
            bodylinearVelocity.Y = linearVelocity.Y + linearVelocityBias.Y;

            #endregion

            #region INLINE: Vector2.Multiply(ref bodylinearVelocity, dt, out dp);

            dp.X = bodylinearVelocity.X*dt;
            dp.Y = bodylinearVelocity.Y*dt;

            #endregion

            _previousPosition = position;

            #region INLINE: Vector2.Add(ref previousPosition, ref dp, out position);

            position.X = _previousPosition.X + dp.X;
            position.Y = _previousPosition.Y + dp.Y;

            #endregion

            linearVelocityBias.X = 0;
            linearVelocityBias.Y = 0;

            //angular
            bodyAngularVelocity = angularVelocity + angularVelocityBias;
            rotationChange = bodyAngularVelocity*dt;
            _previousRotation = rotation;
            rotation = _previousRotation + rotationChange;

            //clamp rotation to 0 <= rotation <2Pi
            while (rotation > MathHelper.TwoPi)
            {
                rotation -= MathHelper.TwoPi;
                ++Revolutions;
            }
            while (rotation <= 0)
            {
                rotation += MathHelper.TwoPi;
                --Revolutions;
            }
            TotalRotation = rotation + Revolutions*MathHelper.TwoPi;
            angularVelocityBias = 0; //reset angVelBias to zero

            if (Updated != null)
            {
                Updated(ref position, ref rotation);
            }
        }

        internal void ApplyImpulseAtWorldOffset(ref Vector2 impulse, ref Vector2 offset)
        {
            #region INLINE: Vector2.Multiply(ref impulse, inverseMass, out dv);

            dv.X = impulse.X*inverseMass;
            dv.Y = impulse.Y*inverseMass;

            #endregion

            #region INLINE: Vector2.Add(ref dv, ref linearVelocity, out linearVelocity);

            linearVelocity.X = dv.X + linearVelocity.X;
            linearVelocity.Y = dv.Y + linearVelocity.Y;

            #endregion

            #region INLINE: Calculator.Cross(ref offset, ref impulse, out angularImpulse);

            float angularImpulse = offset.X*impulse.Y - offset.Y*impulse.X;

            #endregion

            angularImpulse *= inverseMomentOfInertia;
            angularVelocity += angularImpulse;
        }

        internal void ApplyBiasImpulseAtWorldOffset(ref Vector2 impulseBias, ref Vector2 offset)
        {
            #region INLINE: Vector2.Multiply(ref impulseBias, inverseMass, out dv);

            dv.X = impulseBias.X*inverseMass;
            dv.Y = impulseBias.Y*inverseMass;

            #endregion

            #region INLINE: Vector2.Add(ref dv, ref linearVelocityBias, out linearVelocityBias);

            linearVelocityBias.X = dv.X + linearVelocityBias.X;
            linearVelocityBias.Y = dv.Y + linearVelocityBias.Y;

            #endregion

            float angularImpulseBias;

            #region INLINE: Calculator.Cross(ref offset, ref impulseBias, out angularImpulseBias);

            angularImpulseBias = offset.X*impulseBias.Y - offset.Y*impulseBias.X;

            #endregion

            angularImpulseBias *= inverseMomentOfInertia;
            angularVelocityBias += angularImpulseBias;
        }

        protected virtual void Dispose(bool disposing)
        {
            //subclasses can override incase they need to dispose of resources
            //otherwise do nothing.
            if (!isDisposed)
            {
                if (disposing)
                {
                    //dispose managed resources
                }

                //dispose unmanaged resources
            }
            isDisposed = true;
            if (Disposed != null)
            {
                Disposed(this, null);
            }
            //base.Dispose(disposing)        
        }

        #region IntegeratePosition variables

        private float bodyAngularVelocity;
        private Vector2 bodylinearVelocity;
        private Vector2 dp;
        private float rotationChange;

        #endregion

        #region IntegerateVelocity variables

        private Vector2 acceleration = Vector2.Zero;
        private Vector2 dv = Vector2.Zero; //change in linear velocity
        private float dw; //change in angular velocity 

        #endregion

        #region ApplyDrag variables

        //Note: Cleanup. Not used
        //private Vector2 dragDirection = Vector2.Zero;
        private Vector2 _linearDrag = Vector2.Zero;
        private Vector2 _quadraticDrag = Vector2.Zero;
        private float _rotationalDrag;
        private float _speed;
        private Vector2 _totalDrag = Vector2.Zero;

        #endregion

        #region ApplyForceAtLocalPoint variables

        private Vector2 diff;

        #endregion

        #region GetVelocityAtPoint variables

        private Vector2 r1 = Vector2.Zero;

        #endregion

        #region GetLocalPosition variables

        private Vector2 localPositionTemp = Vector2.Zero;

        #endregion

        #region GetBodyMatrix variables

        private Matrix bodyMatrixTemp = Matrix.Identity;
        private Matrix rotationMatrixTemp = Matrix.Identity;
        private Matrix translationMatrixTemp = Matrix.Identity;

        #endregion
    }
}