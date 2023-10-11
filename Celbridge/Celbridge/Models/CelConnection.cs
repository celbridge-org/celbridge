using System;

namespace Celbridge.Models
{
    public class CelConnection : IComparable<CelConnection>, IEquatable<CelConnection>
    {
        public Guid CelConnectionId { get; private set; }

        public ICelScriptNode CelScriptNodeA { get; private set; }
        public ICelScriptNode CelScriptNodeB { get; private set; }

        public CelConnection(ICelScriptNode a, ICelScriptNode b)
        {
            // The connection object between any two nodes is always
            // identical, regardless of the order of a & b

            CelConnectionId = CreateCelConnectionId(a.Id, b.Id);

            if (a.Id.CompareTo(b.Id) < 0)
            {
                CelScriptNodeA = a;
                CelScriptNodeB = b;
            }
            else
            {
                CelScriptNodeA = b;
                CelScriptNodeB = a;
            }
        }

        public int CompareTo(CelConnection? other)
        {
            // If other is not a valid object reference, this instance is greater.
            if (other is null)
            {
                return 1;
            }

            return CelConnectionId.CompareTo(other.CelConnectionId);
        }

        public static bool operator > (CelConnection operand1, CelConnection operand2)
        {
            return operand1.CompareTo(operand2) > 0;
        }

        public static bool operator < (CelConnection operand1, CelConnection operand2)
        {
            return operand1.CompareTo(operand2) < 0;
        }

        public static bool operator >= (CelConnection operand1, CelConnection operand2)
        {
            return operand1.CompareTo(operand2) >= 0;
        }

        public static bool operator <= (CelConnection operand1, CelConnection operand2)
        {
            return operand1.CompareTo(operand2) <= 0;
        }

        public bool Equals(CelConnection? other)
        {
            if (other is null)
            {
                return false;
            }

            if (this.CelConnectionId == other.CelConnectionId)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public override bool Equals(Object? obj)
        {
            if (obj is null)
            { 
                return false;
            }

            CelConnection? other = obj as CelConnection;
            if (other is null)
            {
                return false;
            }
            else
            {
                return Equals(other);
            }
        }

        public override int GetHashCode()
        {
            return this.CelConnectionId.GetHashCode();
        }

        public static bool operator == (CelConnection connection1, CelConnection connection2)
        {
            if (((object)connection1) == null || ((object)connection2) == null)
            {
                return Object.Equals(connection1, connection2);
            }

            return connection1.Equals(connection2);
        }

        public static bool operator != (CelConnection connection1, CelConnection connection2)
        {
            if (((object)connection1) == null || ((object)connection2) == null)
            {
                return !Object.Equals(connection1, connection2);
            }

            return !(connection1.Equals(connection2));
        }

        // Returns the same merged Guid regardless of the order of celIdA & celIdB.
        public static Guid CreateCelConnectionId(Guid celIdA, Guid celIdB)
        {
            Guid a, b;
            if (celIdA.CompareTo(celIdB) < 0)
            {
                a = celIdA;
                b = celIdB;
            }
            else
            {
                a = celIdB;
                b = celIdA;
            }

            const int byteCount = 16;
            byte[] destByte = new byte[byteCount];
            byte[] guid1Byte = a.ToByteArray();
            byte[] guid2Byte = b.ToByteArray();

            for (int i = 0; i < byteCount; i++)
            {
                destByte[i] = (byte)(guid1Byte[i] ^ guid2Byte[i]);
            }
            return new Guid(destByte);
        }
    }
}
