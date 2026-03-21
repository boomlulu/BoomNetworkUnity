#!/bin/bash
set -e

UNITY="/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity"
PROJECT="/Users/boom/Demo/BoomNetworkUnity/Untiy"
RESULTS="/Users/boom/Demo/BoomNetworkUnity/test-results"
SVR_DIR="/Users/boom/Demo/BoomNetwork/svr"
mkdir -p "$RESULTS"

MODE="${1:-edit}" # edit, play, all

start_server() {
    lsof -ti:9000 2>/dev/null | xargs kill -9 2>/dev/null || true
    sleep 0.3
    cd "$SVR_DIR"
    go run ./cmd/framesync/ -addr=:9000 -ppr=2 > /dev/null 2>&1 &
    SERVER_PID=$!
    sleep 1
    echo "  Go server started (pid=$SERVER_PID)"
}

stop_server() {
    lsof -ti:9000 2>/dev/null | xargs kill -9 2>/dev/null || true
    echo "  Go server stopped"
}

echo "=========================================="
echo "  Unity CLI Test Runner"
echo "  Mode: $MODE"
echo "=========================================="

if [ "$MODE" = "edit" ] || [ "$MODE" = "all" ]; then
    echo ""
    echo "[EditMode] Running Codec + Framing tests..."
    "$UNITY" \
        -batchmode \
        -nographics \
        -projectPath "$PROJECT" \
        -runTests \
        -testPlatform EditMode \
        -testResults "$RESULTS/editmode.xml" \
        -logFile "$RESULTS/editmode.log" \
        2>&1

    if grep -q 'result="Failed"' "$RESULTS/editmode.xml" 2>/dev/null; then
        echo "  ✗ EditMode FAILED"
        grep 'message=' "$RESULTS/editmode.xml" | head -5
        [ "$MODE" = "edit" ] && exit 1
    else
        PASSED=$(grep -c 'result="Passed"' "$RESULTS/editmode.xml" 2>/dev/null || echo 0)
        echo "  ✓ EditMode: $PASSED tests passed"
    fi
fi

if [ "$MODE" = "play" ] || [ "$MODE" = "all" ]; then
    echo ""
    echo "[PlayMode] Running FrameSync integration tests..."

    # 每次重启服务器确保状态干净
    start_server

    "$UNITY" \
        -batchmode \
        -nographics \
        -projectPath "$PROJECT" \
        -runTests \
        -testPlatform PlayMode \
        -testResults "$RESULTS/playmode.xml" \
        -logFile "$RESULTS/playmode.log" \
        2>&1

    stop_server

    if grep -q 'test-case.*result="Failed"' "$RESULTS/playmode.xml" 2>/dev/null; then
        PASSED=$(grep -c 'result="Passed"' "$RESULTS/playmode.xml" 2>/dev/null || echo 0)
        FAILED=$(grep -c 'test-case.*result="Failed"' "$RESULTS/playmode.xml" 2>/dev/null || echo 0)
        echo "  ✗ PlayMode: $PASSED passed, $FAILED failed"
        grep -B1 'message=' "$RESULTS/playmode.xml" | grep 'message=' | head -5
        [ "$MODE" = "play" ] && exit 1
    else
        PASSED=$(grep -c 'result="Passed"' "$RESULTS/playmode.xml" 2>/dev/null || echo 0)
        echo "  ✓ PlayMode: $PASSED tests passed"
    fi
fi

echo ""
echo "=========================================="
echo "  Results saved to: $RESULTS/"
echo "=========================================="
