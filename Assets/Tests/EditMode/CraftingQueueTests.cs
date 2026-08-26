using NUnit.Framework;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    public class CraftingQueueTests
    {
        private CraftingQueue queue;

        [SetUp]
        public void Setup()
        {
            queue = new CraftingQueue();
        }

        [Test]
        public void WorkerAssignment_AllowsUpTo4WorkersOnly()
        {
            Assert.AreEqual(0, queue.AssignedWorkerCount);

            Assert.IsTrue(queue.AssignWorker());
            Assert.AreEqual(1, queue.AssignedWorkerCount);

            Assert.IsTrue(queue.AssignWorker());
            Assert.AreEqual(2, queue.AssignedWorkerCount);

            Assert.IsTrue(queue.AssignWorker());
            Assert.AreEqual(3, queue.AssignedWorkerCount);

            Assert.IsTrue(queue.AssignWorker());
            Assert.AreEqual(4, queue.AssignedWorkerCount);
            Assert.IsTrue(queue.IsWorkerFull);

            // 5th worker must be rejected
            Assert.IsFalse(queue.AssignWorker());
            Assert.AreEqual(4, queue.AssignedWorkerCount);
        }

        [Test]
        public void WorkerUnassignment_ClampsAtZero()
        {
            queue.AssignWorker();
            Assert.AreEqual(1, queue.AssignedWorkerCount);

            Assert.IsTrue(queue.UnassignWorker());
            Assert.AreEqual(0, queue.AssignedWorkerCount);

            // Cannot unassign below 0
            Assert.IsFalse(queue.UnassignWorker());
            Assert.AreEqual(0, queue.AssignedWorkerCount);
        }

        [Test]
        public void SpeedMultipliers_ReflectWorkerCountAccurately()
        {
            Assert.AreEqual(0.0f, CraftingQueue.CalculateSpeedMultiplier(0), 0.001f);
            Assert.AreEqual(1.0f, CraftingQueue.CalculateSpeedMultiplier(1), 0.001f);
            Assert.AreEqual(1.75f, CraftingQueue.CalculateSpeedMultiplier(2), 0.001f);
            Assert.AreEqual(2.4f, CraftingQueue.CalculateSpeedMultiplier(3), 0.001f);
            Assert.AreEqual(3.0f, CraftingQueue.CalculateSpeedMultiplier(4), 0.001f);
        }

        [Test]
        public void CraftingProgress_CompletesTaskWhenProgressReachesDuration()
        {
            CraftingTask completedTask = null;
            queue.OnTaskCompleted += (t) => completedTask = t;

            CraftingTask task = queue.EnqueueItem(ItemType.Sword, 1);
            float duration = task.TargetDurationSeconds;

            queue.AssignWorker(); // 1 worker = 1.0x speed

            // Advance halfway
            queue.Tick(duration * 0.5f);
            Assert.IsNull(completedTask);
            Assert.AreEqual(1, queue.Queue.Count);
            Assert.AreEqual(0.5f, task.NormalizedProgress, 0.01f);

            // Advance remainder
            queue.Tick(duration * 0.5f);
            Assert.IsNotNull(completedTask);
            Assert.AreEqual(task.TaskId, completedTask.TaskId);
            Assert.AreEqual(0, queue.Queue.Count);
        }

        [Test]
        public void CraftingProgress_ZeroWorkersProducesNoProgress()
        {
            CraftingTask task = queue.EnqueueItem(ItemType.Armor, 1);
            Assert.AreEqual(0, queue.AssignedWorkerCount);

            queue.Tick(10.0f);
            Assert.AreEqual(0f, task.ProgressSeconds, 0.001f);
            Assert.AreEqual(1, queue.Queue.Count);
        }

        [Test]
        public void MultipleWorkers_AccelerateCraftingTime()
        {
            queue.SetWorkerCount(4); // 3.0x speed
            CraftingTask task = queue.EnqueueItem(ItemType.Sword, 1);
            float duration = task.TargetDurationSeconds;

            // Tick for 1/3 duration should complete it
            queue.Tick(duration / 3.0f);
            Assert.AreEqual(0, queue.Queue.Count, "4 workers at 3.0x speed should complete the item in 1/3 time.");
        }

        [Test]
        public void TaskCancellation_RemovesTaskFromQueue()
        {
            CraftingTask task1 = queue.EnqueueItem(ItemType.Sword, 1);
            CraftingTask task2 = queue.EnqueueItem(ItemType.Bow, 1);
            Assert.AreEqual(2, queue.Queue.Count);

            bool cancelled = queue.CancelTask(task1.TaskId);
            Assert.IsTrue(cancelled);
            Assert.AreEqual(1, queue.Queue.Count);
            Assert.AreEqual(task2.TaskId, queue.ActiveTask.TaskId);
        }
    }
}
