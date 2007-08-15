function Font(name, size)
    result = {}
    result.metrics = font.getMetrics(name, size)
    result.metrics.firstChar = math.max(32, result.metrics.firstChar)
    result.metrics.lastChar = math.min(255, result.metrics.lastChar)
    result.characters = {}
    for c = result.metrics.firstChar,result.metrics.lastChar do
        local ch = font.getCharacter(c, name, size)
        result.characters[c] = ch
    end
    return result
end

function drawString(context, font, text, x, y)
    local init_x = x
    for c = 1,#text do
        local ch = text:byte(c)
        local i = font.characters[ch]
        if i then
            local cx = x + i.metrics.glyphOriginX
            local cy = y + (font.metrics.height - i.metrics.glyphOriginY)
            context:drawImage(i, cx, cy)
            x = x + i.metrics.cellIncX
        else
            if ch == 10 then
                x = init_x
                y = y + font.metrics.height
            end
        end
    end
end

function runTest()
    frameRate = 60
    w = Window(640, 480)
    gr = w.glContext
    last_second = 0
    fps = 0
    next_fps = 0
    t_lastframe = 0
    f = Font("Consolas", 24)
    w.onClose = function ()
        quit()
    end
    w.onTick = function (absolute, elapsed)
        local t_start = os.clock()
        gr:clear()
        drawString(gr, f, fps .. " FPS / " .. t_lastframe .. "�S/f", 0, 0)
        local mx, my = w:getMouseState()
        drawString(gr, f, "mouse @ " .. mx .. ", " .. my, 0, f.metrics.height)
        drawString(gr, f, [[The quick brown fox
jumped over the lazy dogs.
multiline text, hurrah!]], 0, f.metrics.height * 2)
        gr:flip()
        next_fps = next_fps + 1
        if (os.clock() - last_second > 1.0) then
            last_second = os.clock()
            fps = next_fps
            next_fps = 0
        end
        local t_end = os.clock()
        t_lastframe = math.floor((t_end - t_start) * 1000000)
    end
    w.onKeyDown = function (key)
        print(wm.getKeyName(key))
    end
    w.tickRate = 1000 / frameRate
    w.vsync = false
    while (wm.poll(true)) do end
end

runTest()