# Demo02 实体权威同步 — 验收流程

## 前提

1. Go 服务器已启动（**不加 -autoroom**）：
   ```
   cd /Users/boom/Demo/BoomNetwork/svr
   go run ./cmd/framesync/ -config=cmd/framesync/config.yaml
   ```
   或通过 Unity: BoomNetwork > Server Window > Start Server

2. Unity 打开 Demo02 场景

## 验收步骤

### Step 1: 连接
1. 在 EntitySyncDemoManager Inspector 中看到两个 PersonSlot（WASD + Arrows）
2. 点击 **Connect All** → 两个 slot 状态变为 Connected，pid 显示 P1, P2

### Step 2: 建房入房
3. 点击 **Create Room** → logText 显示 "Room X created"
4. 点击 **Join All** → 两个 slot 状态变为 InRoom

### Step 3: 开始帧同步
5. 点击 **Start Game** → logText 显示 "FrameSync started"
6. 屏幕上出现两个彩色方块（绿色 WASD、蓝色 Arrows）

### Step 4: 验证本地移动
7. 按 **WASD** → 绿色方块移动，方向箭头跟随
8. 按 **方向键** → 蓝色方块移动

### Step 5: 验证实体状态同步
9. 在 Console 中搜索 `[Demo02]` 日志：
   - 应看到 "Entity sync setup: P1 is authority"
   - 应看到 "Entity sync setup: P2 is authority"

### Step 6: 验证 NetworkTransformSync（Inspector）
10. 在 Hierarchy 中选择一个 PlayerEntity
11. Inspector 中查看 NetworkTransformSync 组件：
    - **IsAuthority** = true（本地控制的实体）
    - **LogicalPosition** 跟随 transform.position
    - **LogicalVelocity** 非零（移动时）

### Step 7: 验证 ParrelSync（如有）
12. 打开 ParrelSync 克隆编辑器
13. Editor A: WASD 移动 → Editor B 应看到该角色**平滑跟随**
14. Editor B: 方向键移动 → Editor A 应看到该角色**平滑跟随**
15. 急转弯时：远端角色应**减速→转向→加速**，不硬切

## 预期结果

| 项目 | 预期 |
|------|------|
| 编译 | 无报错 |
| 连接/入房/开始 | 一路顺畅，无卡顿 |
| 本地实体 | WASD/Arrows 立即响应 |
| 远端实体（ParrelSync） | 平滑跟随，方向变化自然过渡 |
| NetworkTransformSync.CorrectionCount | 远端实体 > 0（证明纠偏在工作） |
| ServerWindow Messages 页 | 可看到 SendEntityState / PushEntityState 消息 |
| 回归测试 | 7/7 通过 |
