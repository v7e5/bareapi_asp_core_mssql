#!/usr/bin/node
const cl = console.log;
const wl = s => process.stdout.write(s.toString())

import {readFileSync} from 'node:fs'

;(_f => {

  wl(
    JSON.stringify({q: readFileSync(_f, 'utf-8')})
  )

})('./misc/q.sql')
