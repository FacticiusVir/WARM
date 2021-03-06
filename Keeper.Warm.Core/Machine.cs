﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Keeper.Warm
{
    public class Machine
    {
        private Dictionary<MethodToken, MethodInfo> methods = new Dictionary<MethodToken, MethodInfo>();

        public void DefineMethod(MethodToken token, Opcode[] code, MethodToken[] methodTable = null, int localCount = 0)
        {
            var methodInfo = new MethodInfo()
            {
                Code = code,
                MethodTable = methodTable,
                LocalCount = localCount
            };

            this.methods.Add(token, methodInfo);
        }

        public Thread SpawnThread(MethodToken entryPoint, params long[] initialStack)
        {
            return new Thread(this, entryPoint, initialStack, 1024);
        }

        private static int GetInstructionSize(Opcode opcode)
        {
            Opcode operandSizeFlag
                = opcode & Opcode.OperandMask;

            switch (operandSizeFlag)
            {
                case Opcode.NoOperand:
                    return 1;
                case Opcode.Int64Operand:
                case Opcode.MethodTokenOperand:
                    return 2;
                default:
                    throw new NotSupportedException();
            }
        }

        public class Thread
        {
            private Machine parent;
            private WritableStack<Word> stack;
            private Stack<MethodStackFrame> methodStack = new Stack<MethodStackFrame>();
            private Word[] heap;

            public Thread(Machine parent, MethodToken entryPoint, long[] initialStack, int heapSize)
            {
                this.parent = parent;
                this.stack = new WritableStack<Word>(initialStack.Select(x => new Word { Int64 = x }));
                this.heap = new Word[heapSize];

                var entryPointInfo = this.parent.methods[entryPoint];

                this.methodStack.Push(
                    new MethodStackFrame(this.stack, entryPointInfo.LocalCount, entryPoint.ReturnValues)
                    {
                        Token = entryPoint
                    });
            }

            public StepResult Step()
            {
                if (!this.methodStack.Any())
                {
                    return StepResult.Halt;
                }

                var opCodes = this.parent.methods[this.CurrentFrame.Token];

                if (this.CurrentFrame.InstructionPointer > opCodes.Code.Length)
                {
                    return StepResult.Halt;
                }

                Opcode currentOpcode = opCodes.Code[this.CurrentFrame.InstructionPointer];

                int opcodeStep = GetInstructionSize(currentOpcode);

                int nextInstructionPointer = this.CurrentFrame.InstructionPointer + opcodeStep;

                int opcodeSuffix = (int)currentOpcode & 0xFF;

                int operand = opcodeStep > 1
                                ? (int)opCodes.Code[this.CurrentFrame.InstructionPointer + 1]
                                : 0;

                System.Diagnostics.Debug.WriteLine(currentOpcode);

                switch (currentOpcode)
                {
                    case Opcode.Add:
                        this.Arithmetic(Word.Add);
                        break;
                    case Opcode.And:
                        this.Arithmetic(Word.And);
                        break;
                    case Opcode.Call:
                        int tokenIndex = (int)opCodes.Code[this.CurrentFrame.InstructionPointer + 1];
                        var callTarget = opCodes.MethodTable[tokenIndex];
                        this.CurrentFrame.InstructionPointer = nextInstructionPointer;
                        this.Call(callTarget);
                        return StepResult.Continue;
                    case Opcode.Duplicate:
                        this.Duplicate();
                        break;
                    case Opcode.Halt:
                        return StepResult.Halt;
                    case Opcode.Increment:
                        this.Arithmetic(Word.Increment);
                        break;
                    case Opcode.Load:
                        this.Load();
                        break;
                    case Opcode.LoadArgumentAddress0:
                    case Opcode.LoadArgumentAddress1:
                    case Opcode.LoadArgumentAddress2:
                    case Opcode.LoadArgumentAddress3:
                    case Opcode.LoadArgumentAddress4:
                    case Opcode.LoadArgumentAddress5:
                    case Opcode.LoadArgumentAddress6:
                    case Opcode.LoadArgumentAddress7:
                        this.LoadArgumentAddress(opcodeSuffix);
                        break;
                    case Opcode.LoadConstant:
                        this.LoadConstant(new Word { Int32 = operand });
                        break;
                    case Opcode.LoadConstant0:
                    case Opcode.LoadConstant1:
                    case Opcode.LoadConstant2:
                    case Opcode.LoadConstant3:
                    case Opcode.LoadConstant4:
                    case Opcode.LoadConstant5:
                    case Opcode.LoadConstant6:
                    case Opcode.LoadConstant7:
                        this.LoadConstant(new Word { Int64 = opcodeSuffix });
                        break;
                    case Opcode.LoadLocal:
                        this.LoadLocal(operand);
                        break;
                    case Opcode.LoadLocal0:
                    case Opcode.LoadLocal1:
                    case Opcode.LoadLocal2:
                    case Opcode.LoadLocal3:
                    case Opcode.LoadLocal4:
                    case Opcode.LoadLocal5:
                    case Opcode.LoadLocal6:
                    case Opcode.LoadLocal7:
                        this.LoadLocal(opcodeSuffix);
                        break;
                    case Opcode.LoadLocalAddress:
                        this.LoadLocalAddress(operand);
                        break;
                    case Opcode.LoadPointerHeap:
                    case Opcode.LoadPointerRetained:
                        this.LoadPointer((AddressType)opcodeSuffix);
                        break;
                    case Opcode.Or:
                        this.Arithmetic(Word.Or);
                        break;
                    case Opcode.Proceed:
                        this.Proceed();
                        return StepResult.Continue;
                    case Opcode.Shl:
                        this.Arithmetic(Word.ShiftLeft);
                        break;
                    case Opcode.Shr:
                        this.Arithmetic(Word.ShiftRight);
                        break;
                    case Opcode.Store:
                        this.Store();
                        break;
                    case Opcode.StoreLocal:
                        this.StoreLocal(operand);
                        break;
                    case Opcode.StoreLocal0:
                    case Opcode.StoreLocal1:
                    case Opcode.StoreLocal2:
                    case Opcode.StoreLocal3:
                    case Opcode.StoreLocal4:
                    case Opcode.StoreLocal5:
                    case Opcode.StoreLocal6:
                    case Opcode.StoreLocal7:
                        this.StoreLocal(opcodeSuffix);
                        break;
                    default:
                        throw new Exception($"Unknown opcode: {currentOpcode}");
                }

                this.CurrentFrame.InstructionPointer = nextInstructionPointer;

                return StepResult.Continue;
            }

            private void Store()
            {
                Word address = this.CurrentFrame.Pop();

                Word value = this.CurrentFrame.Pop();

                this.StoreToAddress(address.Address, value);
            }

            private void Load()
            {
                Word address = this.CurrentFrame.Pop();

                Word value = this.LoadFromAddress(address.Address);

                this.CurrentFrame.Push(value);
            }

            private Word LoadFromAddress(Address address)
            {
                switch (address.Type)
                {
                    case AddressType.Stack:
                        return this.stack[(int)address.Pointer];
                    case AddressType.Heap:
                        return this.heap[(int)address.Pointer];
                    default:
                        throw new NotImplementedException();
                }
            }

            private void StoreToAddress(Address address, Word value)
            {
                switch (address.Type)
                {
                    case AddressType.Stack:
                        this.stack[(int)address.Pointer] = value;
                        break;
                    case AddressType.Heap:
                        this.heap[(int)address.Pointer] = value;
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            private void LoadArgumentAddress(int argumentIndex)
            {
                Word argumentAddress = this.CurrentFrame.GetArgumentAddress(argumentIndex);

                this.stack.Push(argumentAddress);
            }

            private void LoadLocalAddress(int localIndex)
            {
                Word localAddress = this.CurrentFrame.GetLocalAddress(localIndex);

                this.stack.Push(localAddress);
            }

            private void LoadLocal(int localIndex)
            {
                Address localAddress = this.CurrentFrame.GetLocalAddress(localIndex).Address;

                Word value = this.LoadFromAddress(localAddress);

                this.stack.Push(value);
            }

            private void StoreLocal(int localIndex)
            {
                Address localAddress = this.CurrentFrame.GetLocalAddress(localIndex).Address;

                Word value = this.stack.Pop();

                this.StoreToAddress(localAddress, value);
            }

            public void Push(long value)
            {
                this.CurrentFrame.Push(new Word() { Int64 = value });
            }

            private void Proceed()
            {
                this.CurrentFrame.Clear();

                this.methodStack.Pop();
            }

            private void Call(MethodToken callTarget)
            {
                if (!this.parent.methods.ContainsKey(callTarget))
                {
                    throw new Exception("Missing method.");
                }
                else
                {
                    var method = this.parent.methods[callTarget];

                    this.methodStack.Push(
                        new MethodStackFrame(this.stack, method.LocalCount, callTarget.ReturnValues)
                        {
                            Token = callTarget
                        });
                }
            }

            private void Duplicate()
            {
                Word value = this.CurrentFrame.Peek();

                this.CurrentFrame.Push(value);
            }

            private void Arithmetic(Func<Word, Word> @operator)
            {
                Word value = this.CurrentFrame.Pop();
                this.CurrentFrame.Push(@operator(value));
            }

            private void Arithmetic(Func<Word, Word, Word> @operator)
            {
                Word b = this.CurrentFrame.Pop();
                Word a = this.CurrentFrame.Pop();
                this.CurrentFrame.Push(@operator(a, b));
            }

            private void LoadPointer(AddressType addressType)
            {
                Word pointer = this.CurrentFrame.Pop();

                this.CurrentFrame.Push(new Word { Address = new Address(addressType, pointer.Address.Pointer) });
            }

            private void LoadConstant(Word constantValue)
            {
                this.CurrentFrame.Push(constantValue);
            }

            public void SetHeap(int pointer, long value)
            {
                this.heap[pointer] = new Word { Int64 = value };
            }

            private MethodStackFrame CurrentFrame
            {
                get
                {
                    return this.methodStack.Any()
                        ? this.methodStack.Peek()
                        : null;
                }
            }

            public IEnumerable<long> Stack
            {
                get
                {
                    return this.stack.Select(x => x.Int64);
                }
            }

            public IEnumerable<long> Heap
            {
                get
                {
                    return this.heap.Select(x => x.Int64);
                }
            }
        }

        private class MethodInfo
        {
            public Opcode[] Code
            {
                get;
                internal set;
            }

            public int LocalCount
            {
                get;
                internal set;
            }

            public MethodToken[] MethodTable
            {
                get;
                internal set;
            }
        }

        private class MethodStackFrame
        {
            private readonly WritableStack<Word> threadStack;
            private readonly int stackBase;
            private readonly int localCount;
            private readonly int returnCount;

            public MethodStackFrame(WritableStack<Word> threadStack, int localCount = 0, int returnCount = 0)
            {
                this.threadStack = threadStack;
                this.stackBase = this.threadStack.Count - 1;
                this.localCount = localCount;
                this.returnCount = returnCount;
                for (int index = 0; index < localCount; index++)
                {
                    this.threadStack.Push(new Word());
                }
            }

            public MethodToken Token
            {
                get;
                set;
            }

            public int InstructionPointer
            {
                get;
                set;
            }

            public void Push(Word value)
            {
                this.threadStack.Push(value);
            }

            public Word Pop()
            {
                if (this.threadStack.Count <= (this.stackBase + this.localCount + 1))
                {
                    throw new Exception("Invalid stack pop.");
                }
                else
                {
                    return this.threadStack.Pop();
                }
            }

            public Word Peek()
            {
                if (this.threadStack.Count <= (this.stackBase + this.localCount + 1))
                {
                    throw new Exception("Invalid stack peek.");
                }
                else
                {
                    return this.threadStack.Peek();
                }
            }

            public void Clear()
            {
                Word[] returnValues = null;

                if (this.returnCount > 0)
                {
                    returnValues = new Word[this.returnCount];
                }

                for (int index = 0; index < this.returnCount; index++)
                {
                    returnValues[index] = this.threadStack.Pop();
                }

                while (this.threadStack.Count > this.stackBase - this.Token.Arity + 1)
                {
                    this.threadStack.Pop();
                }

                for (int index = 0; index < this.returnCount; index++)
                {
                    this.threadStack.Push(returnValues[index]);
                }
            }

            public Word GetArgumentAddress(int argumentIndex)
            {
                return new Word { Address = new Address(AddressType.Stack, this.stackBase - argumentIndex) };
            }

            public Word GetLocalAddress(int localIndex)
            {
                return new Word { Address = new Address(AddressType.Stack, this.stackBase + localIndex + 1) };
            }
        }
    }
}